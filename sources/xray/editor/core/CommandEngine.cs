using System;
using System.Collections.Generic;

namespace xray.editor_base;

// Port of base/sources/command_engine.{cpp,h,inline.h} and command_transaction.cpp.
// Nested run() calls form one undo unit: children roll back in reverse order,
// then their parent; redo calls the parent, which recreates its nested commands.
// History is published after success, so rejection/cancel preserves the redo branch.
public sealed class CommandEngine : IDisposable
{
    private sealed class Entry(Command command)
    {
        public readonly Command Command = command;
        public readonly List<Entry> Children = new();
        public long Before, After;
        public bool AffectsDocument => Command.AffectsDocument || Children.Exists(c => c.AffectsDocument);
        public void DisposeChildren() { foreach (var child in Children) child.Dispose(); Children.Clear(); }
        public void Dispose() { DisposeChildren(); Command.Dispose(); }
    }

    private readonly List<Entry> history = new();
    private readonly Stack<Entry> building = new();
    private readonly int limit;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private int cursor;
    private long sequence, state;
    private long? saved;
    private Entry? pending;
    private bool previewApplied, previewFailed, undoing, disposed, faulted;

    public CommandEngine(uint stackLimit = 100)
    {
        if (stackLimit == 0 || stackLimit > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(stackLimit));
        limit = (int)stackLimit;
    }
    private bool editingEnabled=true;
    public bool EditingEnabled {get=>editingEnabled;set{CheckOwner();if(pending!=null || building.Count!=0 || undoing)throw new InvalidOperationException("Finish the current command before changing editing ownership.");editingEnabled=value;Changed?.Invoke();}}
    public event System.Action? Changed;
    // A scene participant validates/applies the candidate before history becomes
    // visible. It must leave its previous state intact when it rejects a candidate.
    public System.Action? Synchronize { get; set; }
    public bool CanUndo => EditingEnabled && !disposed && !faulted && pending == null && building.Count == 0 && !undoing && cursor > 0;
    public bool CanRedo => EditingEnabled && !disposed && !faulted && pending == null && building.Count == 0 && !undoing && cursor < history.Count;
    public bool InPreview => pending != null;
    public int UndoCount => cursor;
    public int RedoCount => history.Count - cursor;

    private void CheckOwner()
    {
        if (disposed || owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Closed or foreign command engine");
    }
    private void CheckIdle()
    {
        CheckOwner();
        if (faulted || pending != null || building.Count != 0 || undoing) throw new InvalidOperationException("Command engine is busy or requires a session reset");
    }
    private void Compensate(System.Action restore)
    {
        try { restore(); }
        catch { faulted = true; throw; }
    }

    // The caller owns the root on redo; fresh children belong to this attempt.
    private bool Execute(Entry entry)
    {
        building.Push(entry);
        bool success = false;
        try { success = entry.Command.commit(); return success; }
        finally
        {
            building.Pop();
            if (!success)
            {
                try { UndoChildren(entry); }
                catch { faulted = true; throw; }
                finally { entry.DisposeChildren(); }
            }
        }
    }

    public bool run(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CheckOwner();
        if(!EditingEnabled){command.Dispose();throw new InvalidOperationException("Stop runtime before editing.");}
        if (faulted || pending != null || undoing) throw new InvalidOperationException("Command engine cannot run during preview or undo");
        var entry = new Entry(command);
        bool accepted = false;
        try
        {
            if (!Execute(entry)) return false;
            if (building.Count == 0)
            {
                try { Synchronize?.Invoke(); }
                catch { Compensate(() => Undo(entry)); throw; }
            }
            accepted = true;
            if (building.Count > 0) building.Peek().Children.Add(entry);
            else Publish(entry);
            return true;
        }
        finally { if (!accepted) entry.Dispose(); }
    }

    private void Publish(Entry entry)
    {
        for (int i = cursor; i < history.Count; i++) history[i].Dispose();
        history.RemoveRange(cursor, history.Count - cursor);
        entry.Before = state;
        entry.After = entry.AffectsDocument ? ++sequence : state;
        state = entry.After;
        history.Add(entry);
        cursor++;
        while (history.Count > limit) { history[0].Dispose(); history.RemoveAt(0); cursor--; }
        Changed?.Invoke();
    }

    private static bool SameShape(Entry a, Entry b)
    {
        if (a.Children.Count != b.Children.Count) return false;
        for (int i = 0; i < a.Children.Count; i++)
            if (a.Children[i].Command.GetType() != b.Children[i].Command.GetType() || !SameShape(a.Children[i], b.Children[i])) return false;
        return true;
    }

    private bool Redo(Entry entry)
    {
        var replay = new Entry(entry.Command);
        try
        {
            if (!Execute(replay)) return false;
            if (!SameShape(entry, replay))
            {
                try { Undo(replay); }
                catch { faulted = true; throw; }
                throw new InvalidOperationException("Redo produced a different nested transaction");
            }
            entry.DisposeChildren();
            entry.Children.AddRange(replay.Children);
            replay.Children.Clear();
            return true;
        }
        finally { replay.DisposeChildren(); }
    }

    private void UndoChildren(Entry entry)
    {
        int firstUndone = entry.Children.Count;
        try
        {
            for (int i = entry.Children.Count - 1; i >= 0; i--) { Undo(entry.Children[i]); firstUndone = i; }
        }
        catch
        {
            RestoreChildren(entry, firstUndone);
            throw;
        }
    }

    private void RestoreChildren(Entry entry, int first)
    {
        bool previousUndoing = undoing;
        undoing = false;
        try
        {
            for (int i = first; i < entry.Children.Count; i++)
                if (!Redo(entry.Children[i])) throw new InvalidOperationException("Nested command compensation rejected");
        }
        catch { faulted = true; throw; }
        finally { undoing = previousUndoing; }
    }

    private void Undo(Entry entry)
    {
        bool previousUndoing = undoing;
        undoing = true;
        try
        {
            UndoChildren(entry);
            try { entry.Command.rollback(); }
            catch { RestoreChildren(entry, 0); throw; }
        }
        finally { undoing = previousUndoing; }
    }

    // Signed count is in logical undo units; a nested transaction is one unit.
    public int rollback(int steps)
    {
        CheckIdle();if(!EditingEnabled)throw new InvalidOperationException("Stop runtime before undo/redo.");
        long target = (long)cursor + steps;
        if (steps == 0 || target < 0 || target > history.Count) return 0;
        int done = 0;
        while (done != steps)
        {
            if (steps < 0)
            {
                var entry = history[cursor - 1];
                Undo(entry);
                try { Synchronize?.Invoke(); }
                catch { Compensate(() => { if (!Redo(entry)) throw new InvalidOperationException("Scene undo compensation rejected"); }); throw; }
                cursor--; state = entry.Before; done--;
            }
            else
            {
                var entry = history[cursor];
                if (!Redo(entry)) break;
                try { Synchronize?.Invoke(); }
                catch { Compensate(() => Undo(entry)); throw; }
                cursor++; state = entry.After; done++;
            }
            Changed?.Invoke();
        }
        return done;
    }

    public bool run_preview(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CheckIdle();
        if(!EditingEnabled){command.Dispose();throw new InvalidOperationException("Stop runtime before editing.");}
        pending = new Entry(command);
        previewApplied = previewFailed = false;
        try { command.start_preview(); return preview(); }
        catch { cancel_preview(); throw; }
    }

    public bool preview()
    {
        CheckOwner();
        if (pending == null || faulted || undoing || building.Count != 0) throw new InvalidOperationException("No available command preview");
        if (previewFailed) return false;
        try
        {
            bool success = pending.Command.commit();
            previewApplied |= success;
            previewFailed = !success;
            if (success)
            {
                try { Synchronize?.Invoke(); }
                catch
                {
                    Compensate(() => { Undo(pending); previewApplied = false; Synchronize?.Invoke(); });
                    throw;
                }
            }
            Changed?.Invoke();
            return success;
        }
        catch { previewFailed = true; throw; }
    }

    public void end_preview()
    {
        CheckOwner();
        if (pending == null) throw new InvalidOperationException("No command preview");
        try
        {
            if (!pending.Command.end_preview() || previewFailed) { cancel_preview(); return; }
            var entry = pending;
            pending = null;
            Publish(entry);
        }
        catch { if (pending != null) cancel_preview(); throw; }
    }

    public void cancel_preview()
    {
        CheckOwner();
        if (pending == null) throw new InvalidOperationException("No command preview");
        var entry = pending;
        try { if (previewApplied) { Undo(entry); Synchronize?.Invoke(); } }
        catch { faulted = true; throw; }
        finally { pending = null; entry.Dispose(); Changed?.Invoke(); }
    }

    public void set_saved_flag() { CheckIdle(); saved = state; Changed?.Invoke(); }
    public bool saved_flag() => !disposed && !faulted && pending == null && building.Count == 0 && !undoing && saved == state;
    public bool undo_stack_empty() => cursor == 0;
    public bool redo_stack_empty() => cursor == history.Count;
    public bool stack_empty() => history.Count == 0;
    public bool transaction_started() => building.Count > 0 || undoing;

    public void clear_stack()
    {
        CheckOwner();
        if (building.Count != 0 || undoing) throw new InvalidOperationException("Cannot reset an active command transaction");
        if (pending != null) cancel_preview();
        foreach (var entry in history) entry.Dispose();
        history.Clear(); cursor = 0; saved = null; state = ++sequence; faulted = false;
        Changed?.Invoke();
    }
    public void Dispose() { if (disposed) return; clear_stack(); disposed = true; Changed = null; Synchronize = null; }
}
