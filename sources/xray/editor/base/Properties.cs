using System;
using System.Windows.Forms;
using System.ComponentModel;

namespace xray.editor_base
{
    public interface IPropertyValue
    {
        object GetValue();
        void SetValue(object value);
        Type ValueType { get; }
    }

    public class PropertyValue<T> : IPropertyValue
    {
        private readonly Func<T> _getter;
        private readonly Action<T> _setter;

        public PropertyValue(Func<T> getter, Action<T> setter)
        {
            _getter = getter;
            _setter = setter;
        }

        public object GetValue() => _getter();
        public void SetValue(object value) => _setter((T)Convert.ChangeType(value, typeof(T)));
        public Type ValueType => typeof(T);
    }

    public class PropertyContainer : ICustomTypeDescriptor
    {
        private readonly Dictionary<string, IPropertyValue> _properties = new Dictionary<string, IPropertyValue>();

        public void AddProperty(string name, IPropertyValue value)
        {
            _properties[name] = value;
        }

        // ICustomTypeDescriptor implementation
        public AttributeCollection GetAttributes() => AttributeCollection.Empty;
        public string GetClassName() => null;
        public string GetComponentName() => null;
        public TypeConverter GetConverter() => null;
        public EventDescriptor GetDefaultEvent() => null;
        public PropertyDescriptor GetDefaultProperty() => null;
        public object GetEditor(Type editorBaseType) => null;
        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
        public EventDescriptorCollection GetEvents(Attribute[] attributes) => EventDescriptorCollection.Empty;

        public PropertyDescriptorCollection GetProperties() => GetProperties(null);

        public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            var descriptors = new List<PropertyDescriptor>();
            foreach (var kvp in _properties)
            {
                descriptors.Add(new XRayPropertyDescriptor(kvp.Key, kvp.Value));
            }
            return new PropertyDescriptorCollection(descriptors.ToArray());
        }

        public object GetPropertyOwner(PropertyDescriptor pd) => this;
    }

    internal class XRayPropertyDescriptor : PropertyDescriptor
    {
        private readonly IPropertyValue _value;

        public XRayPropertyDescriptor(string name, IPropertyValue value) : base(name, null)
        {
            _value = value;
        }

        public override Type ComponentType => typeof(PropertyContainer);
        public override bool IsReadOnly => false;
        public override Type PropertyType => _value.ValueType;

        public override bool CanResetValue(object component) => false;
        public override object GetValue(object component) => _value.GetValue();
        public override void ResetValue(object component) { }
        public override void SetValue(object component, object value) => _value.SetValue(value);
        public override bool ShouldSerializeValue(object component) => false;
    }

    public class XRayPropertyGrid : PropertyGrid
    {
        public XRayPropertyGrid()
        {
            // Custom initialization
        }
    }
}
