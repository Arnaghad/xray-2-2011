////////////////////////////////////////////////////////////////////////////
//	Created		: 10.12.2009
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef RAW_FILE_PROPERTY_STRUCT_H_INCLUDED
#define RAW_FILE_PROPERTY_STRUCT_H_INCLUDED

using namespace System;
using namespace System::ComponentModel;

namespace xray
{
	namespace editor
	{
		public ref struct raw_file_property_struct
		{
		
		#pragma region | Initialize |
		
		public:
				raw_file_property_struct(String^ file_path);
				raw_file_property_struct(configs::lua_config_ptr config);

		#pragma endregion
		
		#pragma region |   Fields   |

		private:
			String^					m_name;
			int						m_bits_per_sample;
			int						m_number_of_chanels;
			int						m_samples_per_second;
			int						m_output_bitrate;

			Action<raw_file_property_struct^>^ loaded_callback;

		public:
			String^					m_file_path;

		#pragma endregion 

		#pragma region | Properties |

		public:
			///<summary>
			///song name - summary
			///</summary>
			[BrowsableAttribute(false)]
			property			String^					name
			{
				String^				get	()							{ return m_name; }
				void				set	(String^ value)				{ m_name = value; }
			}

			///<summary>
			///song compression - summary
			///</summary>
			[CategoryAttribute("Raw Options"),	DefaultValueAttribute(16),						DisplayNameAttribute("Bits per sample"),			DescriptionAttribute("object position")]
			property			int						bits_per_sample
			{
				int					get	()							{ return m_bits_per_sample; }
				void				set	(int value)					{ m_bits_per_sample = value; }
			}

			///<summary>
			///song compression - summary
			///</summary>
			[CategoryAttribute("Raw Options"),	DefaultValueAttribute(1),						DisplayNameAttribute("Number of chanels"),			DescriptionAttribute("object position")]
			property			int						number_of_chanels
			{
				int					get	()							{ return m_number_of_chanels; }
				void				set	(int value)					{ m_number_of_chanels = value; }
			}

			///<summary>
			///song compression - summary
			///</summary>
			[CategoryAttribute("Raw Options"),	DefaultValueAttribute(44100),					DisplayNameAttribute("Samples per second"),			DescriptionAttribute("object position")]
			property			int						samples_per_second
			{
				int					get	()							{ return m_samples_per_second; }
				void				set	(int value)					{ m_samples_per_second = value; }
			}

			///<summary>
			///song compression - summary
			///</summary>
			[CategoryAttribute("Raw Options"),	DefaultValueAttribute(256),						DisplayNameAttribute("Output bitrate"),			DescriptionAttribute("object position")]
			property			int						output_bitrate
			{
				int					get	()							{ return m_output_bitrate; }
				void				set	(int value)					{ m_output_bitrate = value; }
			}
			

		#pragma endregion 

		#pragma region |  Methods   |

		private:
			void	on_file_property_loaded		(xray::resources::queries_result& data);

		public:
			void	load						(Action<raw_file_property_struct^>^ on_load_callback);
			void	load						(Action<raw_file_property_struct^>^ on_load_callback, memory::base_allocator* allocator);
			void	save						();

		#pragma endregion

		}; // class raw_file_proprty_struct
	}//namespace editor
}//namespace xray
#endif // #ifndef RAW_FILE_PROPERTY_STRUCT_H_INCLUDED
