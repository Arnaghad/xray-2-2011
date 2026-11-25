using System;
using System.Collections.Generic;
using xray.editor.avalonia_controls.helpers;

namespace xray.editor.avalonia_controls;

public class sizeable_matrix<TItem>
{
    public sizeable_matrix()
    {
        m_inner_matrix = new List<List<TItem>>();
    }

    public sizeable_matrix(int width, int height)
    {
        m_width = width;
        m_height = height;
        m_inner_matrix = new List<List<TItem>>(height);
        for (int i = 0; i < height; ++i)
        {
            var row = new List<TItem>(width);
            for (int j = 0; j < width; ++j)
                row.Add(default!);
            m_inner_matrix.Add(row);
        }
    }

    private readonly List<List<TItem>> m_inner_matrix;
    private int m_width;
    private int m_height;

    public TItem this[int x, int y]
    {
        get => m_inner_matrix[y][x];
        set => m_inner_matrix[y][x] = value;
    }

    public int width => m_width;
    public int height => m_height;

    public bool is_item_exists(int x, int y)
    {
        if (y >= m_inner_matrix.Count || y < 0)
            return false;
        if (x >= m_inner_matrix[y].Count || x < 0)
            return false;
        if (!typeof(TItem).IsValueType && m_inner_matrix[y][x] == null)
            return false;
        return true;
    }

    public void add_row() => insert_row(m_height);
    public void add_row(List<TItem> new_row) => insert_row(m_height, new_row);

    public void insert_row(int index)
    {
        var new_row = new List<TItem>(m_width);
        m_inner_matrix.Insert(index, new_row);
        ++m_height;
        for (int i = 0; i < m_width; ++i)
            new_row.Add(default!);
    }

    public void insert_row(int index, List<TItem> new_row)
    {
        if (m_inner_matrix.Count == 0)
            m_width = new_row.Count;
        if (new_row.Count != m_width)
            throw new ArgumentException($"new_row must contain {m_width} elements.");
        m_inner_matrix.Insert(index, new_row);
        ++m_height;
    }

    public void remove_row_at(int index)
    {
        m_inner_matrix.RemoveAt(index);
        --m_height;
    }

    public void add_column() => insert_column(m_width);
    public void add_column(List<TItem> new_column) => insert_column(m_width, new_column);

    public void insert_column(int index)
    {
        if (m_inner_matrix.Count == 0)
            m_inner_matrix.Add(new List<TItem>());
        foreach (var row in m_inner_matrix)
            row.Insert(index, default!);
        ++m_width;
    }

    public void insert_column(int index, List<TItem> new_column)
    {
        if (m_inner_matrix.Count == 0)
        {
            for (int i = 0; i < new_column.Count; ++i)
                m_inner_matrix.Add(new List<TItem>());
            m_height = new_column.Count;
        }
        if (new_column.Count != m_height)
            throw new ArgumentException($"new_column must contain {m_height} elements.");
        for (int i = 0; i < m_inner_matrix.Count; ++i)
            m_inner_matrix[i].Insert(index, new_column[i]);
        ++m_width;
    }

    public void remove_column_at(int index)
    {
        foreach (var row in m_inner_matrix)
            row.RemoveAt(index);
        --m_width;
    }

    public void clear()
    {
        m_inner_matrix.Clear();
        m_width = 0;
        m_height = 0;
    }
}

public class hierarchy_item_id
{
    public hierarchy_item_id(params int[] ids) => m_ids = ids;

    public hierarchy_item_id(int[] present, params int[] additional)
    {
        debug.assert(additional.Length > 0);
        m_ids = new int[present.Length + additional.Length];
        present.CopyTo(m_ids, 0);
        additional.CopyTo(m_ids, present.Length);
    }

    public hierarchy_item_id(hierarchy_item_id other_id, params int[] additional) : this(other_id.ids, additional) { }

    private readonly int[] m_ids;

    public int[] ids => m_ids;

    public int last_index
    {
        get => m_ids.Length == 0 ? -1 : m_ids[m_ids.Length - 1];
        set
        {
            if (m_ids.Length > 0)
                m_ids[m_ids.Length - 1] = value;
        }
    }
}
