////////////////////////////////////////////////////////////////////////////
//	Created		: 19.01.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace xray.editor.avalonia_controls.animation_setup
{
    internal class animation_channel_partition_type_to_visibility_converter : IValueConverter
    {
        public object Convert(object? value, Type target_type, object? parameter, CultureInfo culture)
        {
            return value != null && parameter != null && value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object? value, Type target_type, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
