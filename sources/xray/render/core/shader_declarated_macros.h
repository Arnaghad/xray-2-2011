////////////////////////////////////////////////////////////////////////////
//	Created		: 18.10.2010
//	Author		: Nikolay Partas
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_CORE_SHADER_DECLARED_MACROS_H_INCLUDED
#define XRAY_RENDER_CORE_SHADER_DECLARED_MACROS_H_INCLUDED

namespace xray {
namespace render {

#define shader_declarated_macros_max_cout 128

typedef fixed_vector< fs_new::virtual_path_string, shader_declarated_macros_max_cout> shader_declarated_macroses_list;

inline bool cut_defines(pcstr text, pcstr start, pcstr& out_end)
{
	if (!start || !*start || !text || !*text)
		return false;
	
	pcstr defines_start = start;
	out_end				= start;
	
	bool is_block_comment = false,
		is_comment		  = false;
	
	while (identity(true))
	{
		if (*--defines_start=='*' && *--defines_start=='/')
		{
			is_block_comment = true;
			is_comment = true;
			break;
		}
		else if (*defines_start=='/' && *--defines_start=='/')
		{
			is_comment = true;
			break;
		}
		if (!*defines_start)
			return false;
	}
	
	if (!is_comment)
		return false;
	
	if (is_block_comment)
	{
		while (identity(true))
		{
			if (*++out_end=='*' && *++out_end=='/')
			{
				--out_end;
				break;
			}
			if (!*out_end)
				return false;
		}
	}
	else
		while (identity(true))
			if (!*++out_end || *++out_end=='\n')
			{
				--out_end;
				break;
			}
	
	return true;
}

inline bool found_shader_declarated_macroses(pcstr shader_source_code, shader_declarated_macroses_list& out_found_macros_names)
{
	pcstr defines_key	= "$DEFINES$:",
		  defines_pos	= strstr(shader_source_code, defines_key),
		  defines_end   = 0;

	if (defines_pos && cut_defines(shader_source_code, defines_pos, defines_end) && defines_end)
	{
		defines_pos += strlen(defines_key);

		std::string_view const sv(defines_pos, defines_end - defines_pos);
		std::string_view const delims(" ,\t\n\r\n");
		size_t pos = 0;

		while (pos < sv.size())
		{
			pos = sv.find_first_not_of(delims, pos);
			if (pos == sv.npos) break;

			size_t end = sv.find_first_of(delims, pos);
			auto token = sv.substr(pos, end - pos);
			if (!token.empty())
				out_found_macros_names.push_back(fs_new::virtual_path_string(token));

			pos = end;
		}
		return out_found_macros_names.size()!=0;
	}
	else
		return false;
}
} // namespace render
} // namespace xray

#endif // #ifndef XRAY_RENDER_CORE_SHADER_DECLARED_MACROS_H_INCLUDED
