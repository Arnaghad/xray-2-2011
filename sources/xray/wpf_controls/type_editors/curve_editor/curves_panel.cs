////////////////////////////////////////////////////////////////////////////
//	Created		: 20.04.2011
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using xray.editor.wpf_controls.panels;

namespace xray.editor.wpf_controls.curve_editor
{
	internal class curves_panel: single_place_panel
	{
		public				curves_panel	( )					
		{
			ClipToBounds					= true;
			m_guide_line_set				= new GuidelineSet( );

			divide_factor_x_min				= Double.MinValue;
			divide_factor_y_min				= Double.MinValue;

			grid_x_scale_multiplier			= 1;
			grid_y_scale_multiplier			= 1;

			is_horizontal_numbers_visible	= true;
			is_vertical_numbers_visible		= true;
		}

		private static readonly Pen m_grid_pen = SdkWorkspaceTheme.Stroke(SdkWorkspaceTheme.GridLine);
		private static readonly Pen m_grid_major_pen = SdkWorkspaceTheme.Stroke(SdkWorkspaceTheme.Divider);
		private static readonly Pen m_grid_zero_pen = SdkWorkspaceTheme.Stroke(SdkWorkspaceTheme.Axis);
		private static readonly Pen m_field_bounds_pen = SdkWorkspaceTheme.Stroke(SdkWorkspaceTheme.Warning);
		private static readonly Typeface m_normal_typeface = new Typeface("Segoe UI");
		private static readonly Typeface m_big_typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

		private readonly			GuidelineSet				m_guide_line_set;

		private						Double						m_current_divide_factor_x;
		private						Double						m_current_divide_factor_y;
		private						String						m_format_string_x;
		private						String						m_format_string_y;

		private						Point						scale							
		{
			get
			{
				return new Point( parent_panel.scale.X * grid_x_scale_multiplier, parent_panel.scale.Y * grid_y_scale_multiplier );
			}
		}
		private						Point						pan								
		{
			get
			{
				return parent_panel.pan;
			}
		}
		internal					curve_editor_panel			parent_panel					
		{
			get;set;
		}

		public						Double						divide_factor_x					
		{
			get
			{
				return m_current_divide_factor_x;
			}
		}
		public						Double						divide_factor_y					
		{
			get
			{
				return m_current_divide_factor_y;
			}
		}	

		public						Double						divide_factor_x_min				
		{
			get;set;
		}
		public						Double						divide_factor_y_min				
		{
			get;set;
		}

		public						Double						grid_x_scale_multiplier			
		{
			get;set;
		}
		public						Double						grid_y_scale_multiplier			
		{
			get;set;
		}

		public						Boolean						is_horizontal_numbers_visible	
		{
			get;set;
		}
		public						Boolean						is_vertical_numbers_visible		
		{
			get;set;
		}


		protected override			void						OnRender				( DrawingContext drawing_context )		
		{
			drawing_context.DrawRectangle( SdkWorkspaceTheme.Canvas, null, new Rect( 0, 0, ActualWidth, ActualHeight ) );

			if( DesignerProperties.GetIsInDesignMode( this ) )
				return;

			Double lines_per_segment_x;
			Double lines_per_segment_y;

			m_current_divide_factor_x	= Math.Max( divide_factor_x_min, grid_helper.scale_to_divide_factor( out lines_per_segment_x, scale.X / 2 ) );
			var step_x					= m_current_divide_factor_x * scale.X;

			m_current_divide_factor_y	= Math.Max( divide_factor_y_min, grid_helper.scale_to_divide_factor( out lines_per_segment_y, scale.Y / 2 ) );
			var step_y					= m_current_divide_factor_y * scale.Y;

			if( lines_per_segment_x == 1 )
				lines_per_segment_x = 10;

			if( lines_per_segment_x == 2 )
				lines_per_segment_x = 5;

			if( lines_per_segment_y == 1 )
				lines_per_segment_y = 10;

			if( lines_per_segment_y == 2 )
				lines_per_segment_y = 5;

			m_guide_line_set.GuidelinesX.Clear	( );
			m_guide_line_set.GuidelinesY.Clear	( );

			var line_count_x = (Int32)( ActualWidth / step_x ) + 2;
			var line_count_y = (Int32)( ActualHeight / step_y ) + 2;

			for ( var i = 0; i < line_count_x; ++i )
				m_guide_line_set.GuidelinesX.Add( step_x * i + ( pan.X % step_x ) + 0.5 );

			for ( var i = 0; i < line_count_y; ++i )
				m_guide_line_set.GuidelinesY.Add( step_y * i + ( pan.Y % step_y ) + 0.5 );

			drawing_context.PushGuidelineSet( m_guide_line_set );

			grid_helper.compute_format_string( m_current_divide_factor_x, out m_format_string_x );
			grid_helper.compute_format_string( m_current_divide_factor_y, out m_format_string_y );

			//Draw vertical lines
			{
				var line_logical_position = (Int32)( -pan.X / step_x );

				for ( var i = 0; i < line_count_x; ++i )
				{
					var	line_x_position			= step_x * i + ( pan.X % step_x );
					var major = line_logical_position % lines_per_segment_x == 0;
					var pen = line_logical_position == 0 ? m_grid_zero_pen : major ? m_grid_major_pen : m_grid_pen;
					drawing_context.DrawLine(pen, new Point(line_x_position, 0), new Point(line_x_position, ActualHeight));

					if( is_horizontal_numbers_visible )
					{
						var text					= new FormattedText( 
							grid_helper.format( m_format_string_x, line_logical_position * m_current_divide_factor_x ),
							CultureInfo.CurrentCulture,
							FlowDirection.LeftToRight,
							major ? m_big_typeface : m_normal_typeface,
							major ? 11 : 9,
							major ? SdkWorkspaceTheme.Text : SdkWorkspaceTheme.MutedText, VisualTreeHelper.GetDpi(this).PixelsPerDip );

						if (line_x_position >= 0 && line_x_position + text.Width + 2 <= ActualWidth && (major || text.Width + 4 <= step_x))
							drawing_context.DrawText(text, new Point(line_x_position + 1, ActualHeight - text.Height - 1));
					}

					++line_logical_position;
				}
			}
			//Draw horizontal lines
			{
				var line_logical_position = (Int32)( -pan.Y / step_y );

				for ( var i = 0; i < line_count_y; ++i )
				{
					var	line_y_position			= step_y * i + ( pan.Y % step_y );
					var major = line_logical_position % lines_per_segment_y == 0;
					var pen = line_logical_position == 0 ? m_grid_zero_pen : major ? m_grid_major_pen : m_grid_pen;
					drawing_context.DrawLine(pen, new Point(0, line_y_position), new Point(ActualWidth, line_y_position));

					if( is_vertical_numbers_visible )
					{
						var text					= new FormattedText(
							grid_helper.format( m_format_string_y, line_logical_position * -m_current_divide_factor_y ),
							CultureInfo.CurrentCulture,
							FlowDirection.LeftToRight,
							major ? m_big_typeface : m_normal_typeface,
							major ? 11 : 9,
							major ? SdkWorkspaceTheme.Text : SdkWorkspaceTheme.MutedText, VisualTreeHelper.GetDpi(this).PixelsPerDip );

						var labelBottom = ActualHeight - (is_horizontal_numbers_visible ? 14 : 0);
						if (line_y_position >= 0 && line_y_position + text.Height <= labelBottom && (major || text.Height + 2 <= step_y))
							drawing_context.DrawText(text, new Point(1, line_y_position));
					}

					++line_logical_position;
				}
			}   
			drawing_context.Pop();

			//Draw field Bounds
			{
				var top_left				= new Point( parent_panel.field_left_limit, parent_panel.field_top_limit );
				var bottom_right			= new Point( parent_panel.field_right_limit, parent_panel.field_bottom_limit );
				var visual_def_range_min	= parent_panel.logical_to_visual_point( top_left );
				var visual_def_range_max	= parent_panel.logical_to_visual_point( bottom_right );

				m_guide_line_set.GuidelinesX.Clear( );
				m_guide_line_set.GuidelinesY.Clear( );

				m_guide_line_set.GuidelinesX.Add( visual_def_range_min.X + 1 );
				m_guide_line_set.GuidelinesX.Add( visual_def_range_max.X + 1 );
				m_guide_line_set.GuidelinesY.Add( visual_def_range_min.Y + 1 );
				m_guide_line_set.GuidelinesY.Add( visual_def_range_max.Y + 1 );
				
				drawing_context.PushGuidelineSet( m_guide_line_set );

				drawing_context.DrawLine( m_field_bounds_pen, new Point( visual_def_range_min.X, 0 ), new Point( visual_def_range_min.X, ActualHeight ) );
				drawing_context.DrawLine( m_field_bounds_pen, new Point( visual_def_range_max.X, 0 ), new Point( visual_def_range_max.X, ActualHeight ) );
				drawing_context.DrawLine( m_field_bounds_pen, new Point( 0, visual_def_range_min.Y ), new Point( ActualWidth, visual_def_range_min.Y ) );
				drawing_context.DrawLine( m_field_bounds_pen, new Point( 0, visual_def_range_max.Y ), new Point( ActualWidth, visual_def_range_max.Y ) );

				drawing_context.Pop();
			}

			base.OnRender( drawing_context );
		}
	}
}
