using System;
using System.Collections;
using System.Collections.Generic;

namespace xray.editor.avalonia_controls;

public class property_container_collection : property_container_base
{
    public property_container_collection(string item_name_template)
    {
        properties = new property_collection(this);
        this.item_name_template = item_name_template;
    }

    public new property_collection properties
    {
        get => (property_collection)base.properties;
        private set => base.properties = value;
    }

    public string item_name_template;

    public event Action<int>? property_added;
    public event Action<int>? property_removed;

    private void on_property_added(int index) => property_added?.Invoke(index);
    private void on_property_removed(int index) => property_removed?.Invoke(index);

    public class property_collection : property_collection_base, IList<property_descriptor>
    {
        internal property_collection(property_container_collection owner) : base(owner)
        {
            m_properties = new List<property_descriptor>();
        }

        private readonly List<property_descriptor> m_properties;

        public override int count => m_properties.Count;

        public static implicit operator List<property_descriptor>(property_collection collection) =>
            collection.m_properties;

        public override property_descriptor add(property_descriptor desc)
        {
            m_properties.Add(desc);
            desc.owner = m_owner;
            ((property_container_collection)m_owner).on_property_added(m_properties.Count - 1);
            return desc;
        }

        public override void remove(property_descriptor desc) => remove(m_properties.IndexOf(desc));

        public void remove(int property_id)
        {
            var desc = m_properties[property_id];
            desc.owner = null;
            desc.owner_property = null;
            m_properties.RemoveAt(property_id);
            ((property_container_collection)m_owner).on_property_removed(property_id);
        }

        public override void clear()
        {
            foreach (var descriptor in m_properties)
            {
                descriptor.owner = null;
                descriptor.owner_property = null;
            }
            m_properties.Clear();
            m_owner.refresh_properties();
        }

        public override bool contains(property_descriptor desc) => m_properties.IndexOf(desc) >= 0;

        public override IEnumerator<property_descriptor> GetEnumerator() => m_properties.GetEnumerator();

        public int Count => m_properties.Count;
        public bool IsReadOnly => false;

        public property_descriptor this[int index]
        {
            get => m_properties[index];
            set => m_properties[index] = value;
        }

        public void Add(property_descriptor item) => add(item);
        public void Clear() => m_properties.Clear();
        public bool Contains(property_descriptor item) => m_properties.Contains(item);
        public void CopyTo(property_descriptor[] array, int arrayIndex) => m_properties.CopyTo(array, arrayIndex);

        public bool Remove(property_descriptor item)
        {
            remove(item);
            return true;
        }

        public int IndexOf(property_descriptor item) => m_properties.IndexOf(item);

        public void Insert(int index, property_descriptor item)
        {
            m_properties.Insert(index, item);
            ((property_container_collection)m_owner).on_property_added(index);
        }

        public void RemoveAt(int index) => remove(index);
    }
}
