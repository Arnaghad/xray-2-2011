using System;
using System.Collections.Generic;

namespace xray.editor.avalonia_controls.animation_setup
{
    public class setup_panel_event_args : EventArgs
    {
        public setup_panel_event_args(string? ch_n, string? i_n, string? pr_n, object? new_val)
        {
            channel_name = ch_n;
            item_names = new List<string?> { i_n };
            property_names = new List<string?> { pr_n };
            new_values = new List<object?> { new_val };
        }

        public setup_panel_event_args(string? ch_n, List<string?> i_n, List<string?> pr_n, List<object?> new_vals)
        {
            channel_name = ch_n;
            item_names = i_n;
            property_names = pr_n;
            new_values = new_vals;
        }

        public string? channel_name;
        public List<string?> item_names;
        public List<string?> property_names;
        public List<object?> new_values;
    }

    public class setup_panel_copy_event_args : EventArgs
    {
        public setup_panel_copy_event_args(string? ch_n)
        {
            channel_name = ch_n;
        }

        public string? channel_name;
    }
}
