namespace xray.editor.avalonia_controls.property_editors.value;

public partial class Boolean_editor : value_editor_base
{
    public Boolean_editor()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_check_box.IsEnabled = !m_property.is_read_only;
        };
    }

    public override void update() => m_property?.invalidate_value();
}
