////////////////////////////////////////////////////////////////////////////
//	Created		: 25.12.2009
//	Author		: Evgeniy Obertyukh
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef SOUND_FILE_ITEM_PROPERTY_H_INCLUDED
#define SOUND_FILE_ITEM_PROPERTY_H_INCLUDED

#include "sound_item_struct.h"

using namespace System;
using namespace System::ComponentModel;
using namespace System::Collections;
using namespace System::Windows::Forms;
using namespace System::Data;
using namespace System::Drawing;


namespace xray
{
	namespace editor
	{
		ref class	inplace_combo_box;
		ref class	sound_file_item;
		ref struct	sound_item_property_struct;

		/// <summary>
		/// Summary for sound_file_item_property
		/// </summary>
		public ref class sound_file_item_property : public System::Windows::Forms::UserControl
		{

		#pragma region | Initialize |

		public:
			sound_file_item_property(void)
			{
				InitializeComponent();
				//
				//TODO: Add the constructor code here
				//
				in_constructor();
			}

		protected:
			/// <summary>
			/// Clean up any resources being used.
			/// </summary>
			~sound_file_item_property()
			{
				if (components)
				{
					delete components;
				}
			}

		private:
			void in_constructor();

		#pragma region Windows Form Designer generated code
			/// <summary>
			/// Required method for Designer support - do not modify
			/// the contents of this method with the code editor.
			/// </summary>
			void InitializeComponent(void)
			{
				this->m_split_container = (gcnew System::Windows::Forms::SplitContainer());
				this->m_split_container_inner = (gcnew System::Windows::Forms::SplitContainer());
				this->m_delete_button = (gcnew System::Windows::Forms::Button());
				this->m_split_container->Panel1->SuspendLayout();
				this->m_split_container->SuspendLayout();
				this->m_split_container_inner->Panel1->SuspendLayout();
				this->m_split_container_inner->SuspendLayout();
				this->SuspendLayout();
				// 
				// m_split_container
				// 
				this->m_split_container->Dock = System::Windows::Forms::DockStyle::Fill;
				this->m_split_container->Location = System::Drawing::Point(0, 0);
				this->m_split_container->Name = L"m_split_container";
				// 
				// m_split_container.Panel1
				// 
				this->m_split_container->Panel1->Controls->Add(this->m_split_container_inner);
				this->m_split_container->Panel1->Padding = System::Windows::Forms::Padding(2, 2, 0, 0);
				// 
				// m_split_container.Panel2
				// 
				this->m_split_container->Panel2->Padding = System::Windows::Forms::Padding(0, 2, 2, 0);
				this->m_split_container->Panel2->ControlAdded += gcnew System::Windows::Forms::ControlEventHandler(this, &sound_file_item_property::m_split_container_Panel2_ControlAdded);
				this->m_split_container->Panel2->ControlRemoved += gcnew System::Windows::Forms::ControlEventHandler(this, &sound_file_item_property::m_split_container_Panel2_ControlRemoved);
				this->m_split_container->Size = System::Drawing::Size(384, 14);
				this->m_split_container->SplitterDistance = 127;
				this->m_split_container->TabIndex = 0;
				// 
				// m_split_container_inner
				// 
				this->m_split_container_inner->Dock = System::Windows::Forms::DockStyle::Fill;
				this->m_split_container_inner->Location = System::Drawing::Point(2, 2);
				this->m_split_container_inner->Name = L"m_split_container_inner";
				// 
				// m_split_container_inner.Panel1
				// 
				this->m_split_container_inner->Panel1->Controls->Add(this->m_delete_button);
				this->m_split_container_inner->Size = System::Drawing::Size(125, 12);
				this->m_split_container_inner->SplitterDistance = 25;
				this->m_split_container_inner->TabIndex = 0;
				// 
				// m_delete_button
				// 
				this->m_delete_button->Dock = System::Windows::Forms::DockStyle::Top;
				this->m_delete_button->Enabled = false;
				this->m_delete_button->FlatStyle = System::Windows::Forms::FlatStyle::Flat;
				this->m_delete_button->Location = System::Drawing::Point(0, 0);
				this->m_delete_button->Name = L"m_delete_button";
				this->m_delete_button->Size = System::Drawing::Size(25, 21);
				this->m_delete_button->TabIndex = 0;
				this->m_delete_button->Text = L"X";
				this->m_delete_button->UseVisualStyleBackColor = true;
				this->m_delete_button->Click += gcnew System::EventHandler(this, &sound_file_item_property::m_delete_button_Click);
				// 
				// sound_file_item_property
				// 
				this->AutoScaleDimensions = System::Drawing::SizeF(6, 13);
				this->AutoScaleMode = System::Windows::Forms::AutoScaleMode::Font;
				this->BorderStyle = System::Windows::Forms::BorderStyle::FixedSingle;
				this->Controls->Add(this->m_split_container);
				this->Name = L"sound_file_item_property";
				this->Size = System::Drawing::Size(384, 14);
				this->m_split_container->Panel1->ResumeLayout(false);
				this->m_split_container->ResumeLayout(false);
				this->m_split_container_inner->Panel1->ResumeLayout(false);
				this->m_split_container_inner->ResumeLayout(false);
				this->ResumeLayout(false);

			}
		private: System::Windows::Forms::SplitContainer^  m_split_container;


#pragma endregion

		#pragma endregion 
		
		#pragma region |   Fields   |

		private:
			System::ComponentModel::IContainer^		components;
			inplace_combo_box^						m_properties_combo_box;

			sound_item_struct::sound_property^		m_selected_property;
private: System::Windows::Forms::SplitContainer^  m_split_container_inner;
private: System::Windows::Forms::Button^  m_delete_button;
		 sound_file_item^						m_parent_sound;

		#pragma endregion

		#pragma region | Properties |

		public:
			property ComboBox::ObjectCollection^	combobox_items
			{
				ComboBox::ObjectCollection^			get();
			}

			property Int32							selected_combobox_index
			{
				Int32								get();
				void								set(Int32 value);
			}

			property sound_item_property_struct^	selected_combobox_item
			{
				sound_item_property_struct^			get();
			}

			property sound_item_struct::sound_property^ selected_property
			{
				sound_item_struct::sound_property^	get();
				void								set(sound_item_struct::sound_property^ value);
			}
			
			property sound_file_item^				parent_sound
			{
				sound_file_item^					get();
				void								set(sound_file_item^ value);
			}

			property ControlCollection^				dependencies
			{
				ControlCollection^					get(){return m_split_container->Panel2->Controls;}
			}

		#pragma endregion 

		#pragma region |   Methods  |

		private:
			void combo_box_selected_index_changed			(Object^ , EventArgs^ );
			void m_split_container_Panel2_ControlAdded		(Object^  sender, ControlEventArgs^  e);
			void m_split_container_Panel2_ControlRemoved	(Object^  sender, ControlEventArgs^  e);
			void m_delete_button_Click						(Object^  sender, EventArgs^  e);

		public:
		
		#pragma endregion

		};//class sound_file_item_property
	}//namespace editor
}//namespace xray

#endif // #ifndef SOUND_FILE_ITEM_PROPERTY_H_INCLUDED
