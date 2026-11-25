namespace xray.editor.avalonia_controls.hypergraph;

public class property : property_editors.property
{
    public property()
    {
        input_enabled = false;
        output_enabled = false;
    }

    public string type_id { get; set; } = string.Empty;
    public bool input_enabled { get; set; }
    public bool output_enabled { get; set; }

    internal property_view? property_view { get; set; }
    public node? node { get; set; }

    public override object? value
    {
        get
        {
            if (descriptors == null || descriptors.Count == 0)
                return null;
            return base.value;
        }
        set
        {
            if (descriptors == null || descriptors.Count == 0)
                return;
            base.value = value;
        }
    }

    public override string ToString() => value?.ToString() ?? string.Empty;
}
