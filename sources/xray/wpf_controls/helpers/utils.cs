////////////////////////////////////////////////////////////////////////////
//	Created		: 17.12.2010
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace xray.editor.wpf_controls
{
	public static class utils
	{
		// Public WPF inheritance carries the owner to nested controls without
		// reflecting into the private, version-specific WinForms AvalonAdapter.
		public static void set_parent_form(DependencyObject element, Form owner)
		{
			if (owner == null) element.ClearValue(HostFormProperty);
			else element.SetValue(HostFormProperty, owner);
		}

		private static readonly DependencyProperty HostFormProperty =
			DependencyProperty.RegisterAttached("HostForm", typeof(Form), typeof(utils),
				new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

		public static		Point		get_screen_mouse_position	(  )									
		{
			utils_helpers.win_point pt;
			utils_helpers.GetCursorPos( out pt );

			return new Point( pt.x, pt.y );
		}
		public static		void		set_screen_mouse_position	( Point pt )							
		{
			utils_helpers.SetCursorPos( (Int32)pt.X, (Int32)pt.Y );
		}
		[DebuggerNonUserCode]
		public static		Object		cast_to						( this Object obj, Type type )			
		{
			if( obj.GetType( ) == type )
				return obj;

			if( obj is i_caster )
				return ( (i_caster)obj ).cast_to( type );
			
			return Convert.ChangeType( obj, type );
		}
		public static		void		swap_values<TType>			( ref TType first, ref TType second )	
		{
			var tmp		= first;
			first		= second;
			second		= tmp;
		}

		public static		Form		new_form_host				( String title, FrameworkElement child )				
		{
			var win_form		= new Form{ Text = title };
			var element_host	= new ElementHost { Dock = DockStyle.Fill, Child = child };
			child.SetValue(HostFormProperty, win_form);
			element_host.Disposed += (sender, args) => child.ClearValue(HostFormProperty);
			win_form.Controls.Add( element_host );

			return win_form;
		}
		public static		Form		get_parent_form				( FrameworkElement element )			
		{
			return element == null ? null : element.GetValue(HostFormProperty) as Form;
		}
	}
}
