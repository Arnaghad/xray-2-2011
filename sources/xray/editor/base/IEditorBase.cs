using System;

namespace xray.editor_base
{
    /// <summary>
    /// Managed equivalent of the native editor_base interface.
    /// Migrated from C++/CLI 'interface class' to pure C#.
    /// </summary>
    public interface IEditorBase
    {
        CommandEngine GetCommandEngine();
        void OnRender();
        void Initialize();
        void Destroy();
        void ClearResources();
        void Tick();
        bool CloseQuery();
        bool Load(string name);
        void Show();
        void LoadSettings();
        void SaveSettings();
        void SetDefaultLayout();
        WeifenLuo.WinFormsUI.Docking.IDockContent FindDockContent(string name);
        string Name { get; }
    }
}
