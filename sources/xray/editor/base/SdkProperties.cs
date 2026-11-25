using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace xray.editor_base;

[StructLayout(LayoutKind.Sequential)]
public struct SdkPosition
{
    public uint Version, Size;
    public ulong Object, Revision;
    public float X, Y, Z;
    public uint Reserved;
    public static SdkPosition Empty => new() { Version = 1, Size = 40 };
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SdkCallbacks
{
    public uint Version, Size;
    public nint Context;
    public uint Flags, Reserved;
    public delegate* unmanaged[Cdecl]<nint, SdkPosition*, int> Read, Write;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SdkBridge
{
    public uint Version, Size;
    public delegate* unmanaged[Cdecl]<nint, uint, int> DiagnosticStep;
}

public static unsafe partial class Bridge
{
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int BindSdk(nint context, SdkCallbacks* callbacks, SdkBridge* result)
    {
        if (callbacks == null || result == null || callbacks->Version != 1 || callbacks->Size != sizeof(SdkCallbacks)
            || callbacks->Reserved != 0 || (callbacks->Flags & ~1u) != 0 || callbacks->Context == 0
            || callbacks->Read == null || callbacks->Write == null || result->Version != 1 || result->Size != sizeof(SdkBridge)) return -1;
        return Guard(() => {
            World(context).BindSdk(*callbacks);
            *result = new SdkBridge { Version = 1, Size = (uint)sizeof(SdkBridge), DiagnosticStep = &SdkDiagnosticStep };
        });
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SdkDiagnosticStep(nint context, uint step) => Guard(() => World(context).SdkDiagnosticStep(step));
}

// Native position adapter; all edits use the shared command engine.
public sealed unsafe class SdkProperties : IDisposable
{
    private SdkCallbacks native;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private SdkPosition current;
    private readonly CommandEngine commands;
    private readonly bool ownsCommands;
    private bool disposed;
    public SdkProperties(SdkCallbacks callbacks, CommandEngine? commandEngine = null)
    {
        native = callbacks;
        current = Read();
        commands = commandEngine ?? new CommandEngine(100);
        ownsCommands = commandEngine == null;
    }
    private void CheckOwner()
    {
        if (disposed || owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Closed/foreign SDK property owner");
    }
    private static void Validate(SdkPosition value)
    {
        if (value.Version != 1 || value.Size != 40 || value.Object == 0 || value.Reserved != 0
            || !float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidOperationException("Invalid SDK position snapshot");
    }
    private SdkPosition Read()
    {
        CheckOwner();
        var value = SdkPosition.Empty;
        if (native.Read(native.Context, &value) < 0) throw new InvalidOperationException("Native selection is unavailable");
        Validate(value); return value;
    }
    private void Apply(SdkPosition value)
    {
        CheckOwner(); Validate(value);
        value.Revision = current.Revision;
        if (native.Write(native.Context, &value) < 0) throw new InvalidOperationException("Native object changed or is unavailable; reopen the selection before editing");
        Validate(value); current = value;
    }
    private void Edit(float x, float y, float z)
    {
        CheckOwner();
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(x), "Position must be finite");
        if (current.X == x && current.Y == y && current.Z == z) return;
        var before = current;
        var after = current; after.X = x; after.Y = y; after.Z = z;
        commands.run(new PositionCommand(this, before, after));
    }
    private sealed class PositionCommand(SdkProperties model, SdkPosition before, SdkPosition after) : Command
    {
        public override bool commit() { model.Apply(after); return true; }
        public override void rollback() => model.Apply(before);
    }
    [Browsable(false)] public CommandEngine Commands => commands;
    [Browsable(false)] public SdkPosition Snapshot { get { CheckOwner(); return current; } }
    [Browsable(false)] public bool CanUndo => !disposed && commands.CanUndo;
    [Browsable(false)] public bool CanRedo => !disposed && commands.CanRedo;
    [Category("Position"), SdkNumericRange(double.NaN, double.NaN, .1, 4, UpdateOnEditComplete = true)]
    public float X { get => current.X; set => Edit(value, current.Y, current.Z); }
    [Category("Position"), SdkNumericRange(double.NaN, double.NaN, .1, 4, UpdateOnEditComplete = true)]
    public float Y { get => current.Y; set => Edit(current.X, value, current.Z); }
    [Category("Position"), SdkNumericRange(double.NaN, double.NaN, .1, 4, UpdateOnEditComplete = true)]
    public float Z { get => current.Z; set => Edit(current.X, current.Y, value); }
    public void Undo()
    {
        CheckOwner(); if (!CanUndo) throw new InvalidOperationException("Nothing to undo");
        if (commands.rollback(-1) != -1) throw new InvalidOperationException("Native undo rejected");
    }
    public void Redo()
    {
        CheckOwner(); if (!CanRedo) throw new InvalidOperationException("Nothing to redo");
        if (commands.rollback(1) != 1) throw new InvalidOperationException("Native redo rejected");
    }
    public void VerifyNative()
    {
        var actual = Read();
        if (actual.Object != current.Object || actual.Revision != current.Revision || actual.X != current.X || actual.Y != current.Y || actual.Z != current.Z)
            throw new InvalidOperationException("UI/native position mismatch");
    }
    public void Dispose() { CheckOwner(); if (ownsCommands) commands.Dispose(); disposed = true; native = default; }
}
