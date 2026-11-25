namespace xray.editor.avalonia_controls.curve_editor.templates;

internal class custom_curve_template : curve_template_base
{
    public custom_curve_template(float_curve template_curve)
    {
        m_template_curve = template_curve;
        m_success = true;
    }

    private readonly float_curve m_template_curve;

    protected override void initialize()
    {
    }

    public override void commit()
    {
        if (curve == null)
            return;

        while (curve.keys.Count > m_template_curve.keys.Count)
            curve.keys[^1].remove();

        for (var i = 0; i < m_template_curve.keys.Count; ++i)
            set_key(i, m_template_curve.keys[i]);
    }
}
