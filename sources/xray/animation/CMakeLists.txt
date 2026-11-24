cmake_minimum_required(VERSION 3.25)

set(ANIMATION_SOURCES
    sources/anim_track.cpp
    sources/anim_track_eval.cpp
    sources/anim_track_io.cpp
    sources/animation_channel_data.cpp
    sources/animation_collection.cpp
    sources/animation_collection_cook.cpp
    sources/animation_controller_set.cpp
    sources/animation_entry_point.cpp
    sources/animation_event_channels.cpp
    sources/animation_group.cpp
    sources/animation_group_action.cpp
    sources/animation_layer.cpp
    sources/animation_library_linkage.cpp
    sources/animation_mix.cpp
    sources/animation_player.cpp
    sources/animation_player_test.cpp
    sources/animation_world.cpp
    sources/base_interpolator.cpp
    sources/bi_spline_bone_animation.cpp
    sources/bi_spline_skeleton_animation.cpp
    sources/bone_animation.cpp
    sources/bone_animation_data.cpp
    sources/bone_matrices_computer.cpp
    sources/bone_names.cpp
    sources/camera_follower.cpp
    sources/check_animation_data.cpp
    sources/cubic_spline_skeleton_animation.cpp
    sources/cubic_spline_skeleton_animation_cook.cpp
    sources/editable_animation_event_channels.cpp
    sources/editor_animation.cpp
    sources/editor_mixer.cpp
    sources/eigen_matrix.cpp
    sources/event_channel.cpp
    sources/fermi_interpolator.cpp
    sources/fixed_bone.cpp
    sources/instant_interpolator.cpp
    sources/interpolator_comparer.cpp
    sources/interpolator_size_calculator.cpp
    sources/linear_interpolator.cpp
    sources/mixing_animation_clip.cpp
    sources/mixing_animation_lexeme.cpp
    sources/mixing_animation_lexeme_parameters.cpp
    sources/mixing_animation_state.cpp
    sources/mixing_binary_tree_addition_node.cpp
    sources/mixing_binary_tree_animation_node.cpp
    sources/mixing_binary_tree_expression_simplifier.cpp
    sources/mixing_binary_tree_multiplication_node.cpp
    sources/mixing_binary_tree_null_weight_searcher.cpp
    sources/mixing_binary_tree_subtraction_node.cpp
    sources/mixing_binary_tree_weight_node.cpp
    sources/mixing_binary_tree_writer.cpp
    sources/mixing_n_ary_tree.cpp
    sources/mixing_n_ary_tree_addition_node.cpp
    sources/mixing_n_ary_tree_animation_event_iterator.cpp
    sources/mixing_n_ary_tree_animation_node.cpp
    sources/mixing_n_ary_tree_animation_time_calculator.cpp
    sources/mixing_n_ary_tree_cloner.cpp
    sources/mixing_n_ary_tree_comparer.cpp
    sources/mixing_n_ary_tree_converter.cpp
    sources/mixing_n_ary_tree_destroyer.cpp
    sources/mixing_n_ary_tree_dumper.cpp
    sources/mixing_n_ary_tree_event_iterator.cpp
    sources/mixing_n_ary_tree_interpolator_selector.cpp
    sources/mixing_n_ary_tree_multiplication_node.cpp
    sources/mixing_n_ary_tree_node_comparer.cpp
    sources/mixing_n_ary_tree_node_constructor.cpp
    sources/mixing_n_ary_tree_redundant_multiplicands_detector.cpp
    sources/mixing_n_ary_tree_size_calculator.cpp
    sources/mixing_n_ary_tree_subtraction_node.cpp
    sources/mixing_n_ary_tree_target_time_scale_calculator.cpp
    sources/mixing_n_ary_tree_time_in_ms_calculator.cpp
    sources/mixing_n_ary_tree_time_scale_calculator.cpp
    sources/mixing_n_ary_tree_time_scale_node.cpp
    sources/mixing_n_ary_tree_time_scale_start_time_modifier.cpp
    sources/mixing_n_ary_tree_time_scale_transition_node.cpp
    sources/mixing_n_ary_tree_transition_to_one_detector.cpp
    sources/mixing_n_ary_tree_transition_tree_constructor.cpp
    sources/mixing_n_ary_tree_weaver.cpp
    sources/mixing_n_ary_tree_weight_calculator.cpp
    sources/mixing_n_ary_tree_weight_event_iterator.cpp
    sources/mixing_n_ary_tree_weight_node.cpp
    sources/mixing_n_ary_tree_weight_transition_node.cpp
    sources/pch.cpp
    sources/poly_curve.cpp
    sources/quaternion_mix.cpp
    sources/resource_cookers.cpp
    sources/roadmap.cpp
    sources/single_animation.cpp
    sources/single_animation_cook.cpp
    sources/skeleton.cpp
    sources/skeleton_animation.cpp
    sources/skeleton_animation_cook.cpp
    sources/skeleton_animation_data.cpp
    sources/skeleton_bone.cpp
    sources/skeleton_cook.cpp
    sources/track_process.cpp
)

add_library(xray_animation STATIC ${ANIMATION_SOURCES})

target_include_directories(xray_animation PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/../../../ 
PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

target_link_libraries(xray_animation PUBLIC xray_core xray_animation_math)

target_compile_definitions(xray_animation PRIVATE XRAY_ANIMATION_BUILDING XRAY_ENGINE_BUILDING)
