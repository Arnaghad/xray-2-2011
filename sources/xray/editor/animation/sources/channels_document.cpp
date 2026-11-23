////////////////////////////////////////////////////////////////////////////
//	Created		: 19.08.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "channels_document.h"
#include "channels_editor.h"
#include "channels_layout_panel.h"
#include "animation_editor.h"

#include <xray/animation/track_processing.h>

#include <xray/animation/api.h>
#include <xray/animation/anim_track_common.h>
#include <xray/animation/i_editor_animation.h>

using xray::animation_editor::channels_document;
using xray::animation_editor::channels_editor;
using namespace xray::editor::wpf_controls;
using namespace xray::animation;

channels_document::channels_document(channels_editor^ ed)
:super(ed->multidocument_base),
m_editor(ed)
{
	SuspendLayout();

	m_channels_label = gcnew System::Windows::Forms::Label();
	m_channels_label->AutoSize = true;
	m_channels_label->Location = System::Drawing::Point(184, 2);
	m_channels_label->Name = L"m_channels_label";
	m_channels_label->Size = System::Drawing::Size(180, 15);
	m_channels_label->TabIndex = 0;
	m_channels_label->Text = L"Select channels:";

	m_bones_list_box = gcnew System::Windows::Forms::ListBox();
	m_bones_list_box->Location = System::Drawing::Point(2, 19);
	m_bones_list_box->Name = L"m_bones_list_box";
	m_bones_list_box->SelectionMode = System::Windows::Forms::SelectionMode::One;
	m_bones_list_box->Size = System::Drawing::Size(180, 10);
	m_bones_list_box->TabIndex = 1;
	m_bones_list_box->Dock = System::Windows::Forms::DockStyle::Left;
	m_bones_list_box->SelectedIndexChanged += gcnew System::EventHandler(this, &channels_document::bones_selection_changed);
	m_bones_list_box->Sorted = true;

	m_channels_list_box = gcnew System::Windows::Forms::ListBox();
	m_channels_list_box->AutoSize = true;
	m_channels_list_box->Location = System::Drawing::Point(184, 19);
	m_channels_list_box->Name = L"m_channels_list_box";
	m_channels_list_box->SelectionMode = System::Windows::Forms::SelectionMode::MultiExtended;
	m_channels_list_box->Size = System::Drawing::Size(180, 10);
	m_channels_list_box->TabIndex = 2;
	m_channels_list_box->Items->Add(enum_channel_id::channel_translation_x);
	m_channels_list_box->Items->Add(enum_channel_id::channel_translation_y);
	m_channels_list_box->Items->Add(enum_channel_id::channel_translation_z);
	m_channels_list_box->Items->Add(enum_channel_id::channel_rotation_x);
	m_channels_list_box->Items->Add(enum_channel_id::channel_rotation_y);
	m_channels_list_box->Items->Add(enum_channel_id::channel_rotation_z);
	m_channels_list_box->Items->Add(enum_channel_id::channel_scale_x);
	m_channels_list_box->Items->Add(enum_channel_id::channel_scale_y);
	m_channels_list_box->Items->Add(enum_channel_id::channel_scale_z);
	m_channels_list_box->Visible = false;
	m_channels_list_box->SelectedIndexChanged += gcnew System::EventHandler(this, &channels_document::channels_selection_changed);

	Controls->Add(m_channels_label);
	Controls->Add(m_bones_list_box);
	Controls->Add(m_channels_list_box);
	ResumeLayout(false);
}

channels_document::~channels_document()
{
	if (components)
		delete components;
}

void channels_document::save()
{
	if(is_saved)
		return;
}

void channels_document::load()
{
	unmanaged_string name = unmanaged_string(Name);
	fs_new::virtual_path_string source_path;
	source_path += unmanaged_string( animation_editor::single_animations_path + "/" ).c_str( );
	source_path += name.c_str();

	fs_new::virtual_path_string check_data_path;
	check_data_path += unmanaged_string( animation_editor::single_animations_path + "/" ).c_str( );
	check_data_path += name.c_str();
	check_data_path += ".clip_check_data";

	xray::resources::request arr[] = {
		{ source_path.c_str(), xray::resources::animation_class },
		{ check_data_path.c_str(), xray::resources::lua_config_class },
	};

	query_result_delegate* rq = NEW(query_result_delegate)(gcnew query_result_delegate::Delegate(this, &channels_document::on_resources_loaded), g_allocator);
	xray::resources::query_resources(
		arr, 2,
		std::bind(&query_result_delegate::callback, rq, std::placeholders::_1),
		g_allocator);
}

void channels_document::on_resources_loaded(xray::resources::queries_result& result)
{
	if(result[0].is_successful() && result[1].is_successful())
	{
		xray::resources::managed_resource_ptr anim_managed_ptr = static_cast_resource_ptr<xray::resources::managed_resource_ptr>(result[0].get_managed_resource());
		xray::configs::lua_config_value check_data_root = static_cast_resource_ptr<xray::configs::lua_config_ptr>(result[1].get_unmanaged_resource())->get_root();


		auto const disc_data = xray::animation::read_check_data(check_data_root);
		xray::animation::i_editor_animation* anim = xray::animation::create_editor_animation(*g_allocator, anim_managed_ptr);
		const u32 num_bones = u32(disc_data.bone_count());
		if (anim->bones_count() != num_bones) {
			xray::animation::destroy_editor_animation(*g_allocator, anim);
			return;
		}

		m_data = gcnew animation_data();
		m_check_data = gcnew animation_data();
		const u32 frame_count = u32(disc_data.frame_count());
		for(u32 bone=0; bone<num_bones; ++bone)
		{
			System::String^ bone_name = gcnew System::String(anim->bone_name(bone));
			m_bones_list_box->Items->Add(bone_name);
			bone_data^ m_bone_data = gcnew bone_data();
			bone_data^ m_bone_check_data = gcnew bone_data();
			for(u32 ch=0; ch<9; ++ch)
			{
				channel_data^ m_channel_data = gcnew channel_data();
				channel_data^ m_channel_check_data = gcnew channel_data();
				for(u32 frame_number=0; frame_number<frame_count; ++frame_number)	
				{
					auto const frame = disc_data.frame(bone, frame_number);
					float const time = disc_data.time(bone, frame_number);
					time_value_pair check_p = time_value_pair(time, frame[ch]);
					m_channel_check_data->Add(check_p);

					float v = xray::math::QNaN; 
					u32 frame_current = 0;
					anim->evaluate(bone, ch, time, v, frame_current);
					time_value_pair p = time_value_pair(time, v);
					m_channel_data->Add(p);
				}
				m_bone_data->Add(enum_channel_id(ch), m_channel_data);
				m_bone_check_data->Add(enum_channel_id(ch), m_channel_check_data);
			}
			m_data->Add(bone_name, m_bone_data);
			m_check_data->Add(bone_name, m_bone_check_data);
		}
		xray::animation::destroy_editor_animation(*g_allocator, anim);
	}
}

void channels_document::undo()
{
}

void channels_document::redo()
{
}

void channels_document::select_all()
{
}

void channels_document::copy(bool del)
{
	XRAY_UNREFERENCED_PARAMETER(del);
}

void channels_document::paste()
{
}

void channels_document::del()
{
}

void channels_document::bones_selection_changed(System::Object^ , System::EventArgs^ )
{
	m_channels_list_box->SelectedItem = nullptr;
	m_editor->layout_panel->add_curves(nullptr);
	if(m_bones_list_box->SelectedItem==nullptr)
		m_channels_list_box->Visible = false;
	else
		m_channels_list_box->Visible = true;
}

void channels_document::channels_selection_changed(System::Object^ , System::EventArgs^ )
{
	if(m_channels_list_box->SelectedItem!=nullptr)
	{
		System::String^ bone_name = safe_cast<System::String^>(m_bones_list_box->SelectedItem);
		System::Collections::Generic::List<float_curve^>^ lst = gcnew System::Collections::Generic::List<float_curve^>();

		for each(enum_channel_id id in m_channels_list_box->SelectedItems)
		{
			System::Collections::Generic::List<float_curve_key^>^ keys = gcnew System::Collections::Generic::List<float_curve_key^>();
			System::Collections::Generic::List<float_curve_key^>^ check_keys = gcnew System::Collections::Generic::List<float_curve_key^>();
			for(int i = 0; i<m_data[bone_name][id]->Count-1; ++i)
			{
				float x = m_data[bone_name][id][i].Key;
				float y = m_data[bone_name][id][i].Value/**100*/;
				keys->Add(gcnew float_curve_key(System::Windows::Point(x, y)));

				float check_x = m_check_data[bone_name][id][i].Key;
				float check_y = m_check_data[bone_name][id][i].Value/**100*/;
				check_keys->Add(gcnew float_curve_key(System::Windows::Point(check_x, check_y)));
			}

			float_curve^ curve = gcnew float_curve(keys);
			curve->name = "curve_"+id.ToString();
			curve->color = System::Windows::Media::Colors::White;
			float_curve^ check_curve = gcnew float_curve(check_keys);
			check_curve->name = "check_curve"+id.ToString();

			lst->Add(curve);
			lst->Add(check_curve);
		}

		m_editor->layout_panel->add_curves(lst);
	}
}
