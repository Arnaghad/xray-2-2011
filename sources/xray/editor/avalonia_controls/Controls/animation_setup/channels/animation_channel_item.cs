using System;
using System.ComponentModel;

namespace xray.editor.avalonia_controls.animation_setup
{
    public class animation_channel_item : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public animation_channel_item(animation_channel ch)
        {
            channel = ch;
            id = Guid.NewGuid();
        }

        protected void on_property_changed(string property_name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
        }

        public virtual void update()
        {
        }

        public virtual void snap_to_frames()
        {
        }

        public virtual void change_property(string property_name, object? value)
        {
        }

        public virtual object? get_property(string property_name)
        {
            return null;
        }

        public Guid id;
        public animation_channel channel;
    }
}
