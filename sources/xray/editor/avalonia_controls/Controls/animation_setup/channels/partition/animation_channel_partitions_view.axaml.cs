using Avalonia.Controls;

namespace xray.editor.avalonia_controls.animation_setup
{
    public partial class animation_channel_partitions_view : UserControl
    {
        public animation_channel_partitions_view()
        {
            InitializeComponent();
            DataContextChanged += delegate
            {
                m_channel = DataContext as animation_channel;
                m_partitions_view.ItemsSource = m_channel?.objects;
            };
        }

        private animation_channel? m_channel;
    }
}
