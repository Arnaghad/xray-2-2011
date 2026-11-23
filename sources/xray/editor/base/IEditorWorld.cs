using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace xray.editor_base
{
    public interface IEditorWorld
    {
        void load();
        void run();
        void tick();
        void clear_resources();
        
        IntPtr main_handle();
        IntPtr view_handle();
        
        void editor_mode(bool value);
        
        // Subsystems
        CommandEngine command_engine();
        InputEngine input_engine();
        
        // UI Helpers
        ImageList images16x16();
        
        void show_options_dialog();
    }
}
