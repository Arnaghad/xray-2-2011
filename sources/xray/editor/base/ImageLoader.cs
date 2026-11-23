using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace xray.editor_base
{
    // Exact enum indices from sources/xray/editor/base/images/images16x16.h
    public enum images16x16
    {
        ambient_light,
        script_object_tree_icon,
        misc_tree_icon,
        sun_light,
        object_light,
        composite_tree_icon,

        folder_closed,
        folder_open,
        folder_closed_modified,
        folder_open_modified,
        node_resource,
        node_resource_modified,
        node_resource_deleted,
        node_material,
        node_material_added,
        node_material_modified,
        node_material_deleted,
        node_texture,
        node_texture_modified,
        node_speedtree,
        node_group,
        node_joint,
        node_mesh,
        node_camera,
        node_sound_file,

        tool_lasso,
        tool_manipulator,
        tool_paint_selection,
        tool_translate,
        tool_rotate,
        tool_scale,
        tool_select,

        terrain_bump,
        terrain_errosion,
        terrain_flatten,
        terrain_noise,
        terrain_raise_lower,
        terrain_ramp,
        terrain_smooth,
        terrain_painter,
        terrain_diffuse_painter,

        mode_editor,
        mode_pause,

        default_object_set,

        reload_shaders,
        terrain_uv_relax,
        terrain_selector,
        pin,

        terrain_debug_mode,
        view_mode_iterator,
        toggle_post_process,
        toggle_draw_grid,

        particle_tree_icon,
        focus_to_origin,
        npc_tree_icon,
        preview_cube,
        preview_sphere,
        setup_shared_data,
        turn_camera_light,
        object_collision,
        view_isolate_selected,
        camera_track,
        camera_wasd_move,
        debug_draw,
        reload_modified_textures,
        show_render_statistics,
        draw_portals,
        make_portals_coplanar,
        create_bsp_tree,
        show_model,
        generate_portals,
        bsp_tree_edge_mode,
        node_group_locked,
        images16x16_count,
    }

    public class ImageLoader
    {
        public static ImageList load_images(string resourceId, string resourceName, uint width, uint shouldBeCount, Assembly assembly)
        {
            ImageList list = new ImageList
            {
                ImageSize = new Size((int)width, (int)width),
                ColorDepth = ColorDepth.Depth32Bit
            };

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceId)
                    ?? assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var temp = new Bitmap(stream);
                    var bitmap = new Bitmap(temp);
                    list.Images.AddStrip(bitmap);
                }
            }
            catch
            {
                // Fallback generated icons if resource stream is unavailable
            }

            while (shouldBeCount > 0 && list.Images.Count < (int)shouldBeCount)
            {
                var bmp = new Bitmap((int)width, (int)width);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(64, 128, 192));
                }
                list.Images.Add(bmp);
            }

            _ = list.Handle;
            return list;
        }

        private static ImageList? s_sharedImages16;

        public ImageList get_shared_images16()
        {
            if (s_sharedImages16 == null)
            {
                s_sharedImages16 = load_images(
                    "xray.editor_base.images16x16.png",
                    "images16x16.png",
                    16,
                    (uint)images16x16.images16x16_count,
                    typeof(ImageLoader).Assembly);
            }
            return s_sharedImages16;
        }
    }
}
