using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using xray.editor_base;
using xray.editor.avalonia_controls;
using xray.editor.avalonia_controls.Controls;
using xray.editor.avalonia_controls.Editors;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.Ide.Dialogs;
using xray.editor.avalonia_controls.Ide.Panels;
using xray.editor.avalonia_controls.Shell;
using xray.editor.avalonia_controls.Shell.Docking;
using xray.editor.avalonia_controls.Themes;
using xray.editor.avalonia_controls.color_matrix_editor;
using xray.editor.avalonia_controls.color_matrix_editor.shaders;
using xray.editor.avalonia_controls.color_picker;
using xray.editor.avalonia_controls.converters;
using xray.editor.avalonia_controls.curve_editor;
using xray.editor.avalonia_controls.property_editors;
using xray.editor.avalonia_controls.property_editors.attributes;
using xray.editor.avalonia_controls.property_editors.item;
using xray.editor.avalonia_controls.property_editors.value;
using xray.editor.avalonia_controls.property_grid;
using xray.editor.avalonia_controls.type_editors;
using xray.editor.avalonia_controls.hypergraph;
using xray.editor.avalonia_controls.data_sources;
using xray.editor.avalonia_controls.logic_view;
using xray.editor.avalonia_controls.animation_playback;
using xray.editor.avalonia_controls.animation_setup;
using xray.editor.avalonia_controls.animation_lexeme_panel;

internal static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static int Main()
    {
        AppBuilder.Configure<SdkAvaloniaApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .SetupWithoutStarting();

        var app = Application.Current as SdkAvaloniaApp;
        Require(app != null, "Expected SdkAvaloniaApp instance after headless setup.");

        // 1. Theme resources check
        Require(app!.TryGetResource(SdkWorkspaceTheme.SurfaceBrushKey, app.ActualThemeVariant, out object? surface) && surface is IBrush,
            "Missing SdkSurfaceBrush resource.");
        Require(app.TryGetResource(SdkWorkspaceTheme.AccentBrushKey, app.ActualThemeVariant, out object? accent) && accent is IBrush,
            "Missing SdkAccentBrush resource.");
        Require(app.TryGetResource(SdkWorkspaceTheme.WarningBrushKey, app.ActualThemeVariant, out object? warning) && warning is IBrush,
            "Missing SdkWarningBrush resource.");

        // 2. SdkAboutView headless host & OK button check
        var aboutView = new SdkAboutView(2011, "2011-2026");
        Require(aboutView.BuildSummary.Contains("build #2011", StringComparison.Ordinal), "Unexpected BuildSummary.");
        bool closeSignaled = false;
        aboutView.CloseRequested += () => closeSignaled = true;

        var aboutWindow = new Window
        {
            Width = 320,
            Height = 200,
            Content = aboutView
        };
        aboutWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var okButton = aboutView.FindControl<Button>("OkButton");
        Require(okButton != null, "Missing OkButton in SdkAboutView.");
        okButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Require(closeSignaled, "SdkAboutView OK click did not raise CloseRequested.");
        aboutWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 3. SdkPropertyValuePreviewControl focus, edit, validation & reset check
        var propControl = new SdkPropertyValuePreviewControl
        {
            PropertyName = "Roughness",
            DefaultValue = "0.50",
            TextValue = "0.50",
            Validator = text => double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out double v) && v >= 0 && v <= 1
                ? null
                : "Value must be between 0 and 1."
        };

        string? committed = null;
        propControl.ValueCommitted += (_, val) => committed = val;

        var propWindow = new Window
        {
            Width = 420,
            Height = 140,
            Content = propControl
        };
        propWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var textBox = propControl.FindControl<TextBox>("ValueTextBox");
        var resetButton = propControl.FindControl<Button>("ResetButton");
        Require(textBox != null && resetButton != null, "Missing child controls in SdkPropertyValuePreviewControl.");
        Require(!propControl.IsModified, "Control should start unmodified.");
        Require(!resetButton!.IsEnabled, "Reset button should be disabled initially.");

        textBox!.Focus();
        textBox.Text = "2.50";
        Dispatcher.UIThread.RunJobs();
        Require(propControl.IsModified, "Control should be marked modified after edit.");
        Require(!propControl.CommitEdit(), "Out-of-range value should fail validation.");
        Require(!string.IsNullOrEmpty(propControl.ValidationError), "ValidationError should be populated.");

        textBox.Text = "0.75";
        Dispatcher.UIThread.RunJobs();
        Require(propControl.CommitEdit(), "Valid value should commit cleanly.");
        Require(committed == "0.75", "ValueCommitted event did not deliver updated value.");
        Require(string.IsNullOrEmpty(propControl.ValidationError), "ValidationError should clear after valid commit.");
        Require(resetButton.IsEnabled, "Reset button should be enabled when modified.");

        resetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Require(!propControl.IsModified && propControl.TextValue == "0.50" && committed == "0.50",
            "Reset did not restore DefaultValue.");

        propWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 4. Wave 2A: color types & utilities roundtrip
        var rgbHdr = new color_rgb(2.0, 1.0, 0.5, 0.8);
        Require(Math.Abs(rgbHdr.multily_factor - 2.0) < 1e-6, "Unexpected HDR multiply factor.");
        var rgbNormalized = rgbHdr;
        rgbNormalized.multiply(0.5);
        var hsv = color_utilities.convert_rgb_to_hsv(rgbNormalized);
        var rgbBack = color_utilities.convert_hsv_to_rgb(hsv);
        Require(Math.Abs(rgbBack.r - rgbNormalized.r) < 1e-3 &&
                Math.Abs(rgbBack.g - rgbNormalized.g) < 1e-3 &&
                Math.Abs(rgbBack.b - rgbNormalized.b) < 1e-3,
            "RGB <-> HSV roundtrip mismatch.");
        Require(color_utilities.generate_hsb_spectrum().Count > 0, "Expected non-empty HSB spectrum stops.");

        // 5. Wave 2A: converters
        var boolToInt = new bool_to_int_converter();
        Require(Equals(boolToInt.Convert(true, typeof(int), null, CultureInfo.InvariantCulture), 1), "bool_to_int_converter failed.");
        var boolToVis = new bool_to_visibility_converter();
        Require(Equals(boolToVis.Convert(false, typeof(bool), null, CultureInfo.InvariantCulture), false), "bool_to_visibility_converter failed.");
        var dblToStr = new Double_to_String_converter();
        Require(Equals(dblToStr.Convert(1.25, typeof(string), null, CultureInfo.InvariantCulture), "1.250"), "Double_to_String_converter failed.");
        var colorToBrush = new color_to_brush_converter();
        Require(colorToBrush.Convert(Colors.OrangeRed, typeof(IBrush), null, CultureInfo.InvariantCulture) is ISolidColorBrush,
            "color_to_brush_converter failed.");
        var singlesToThickness = new singles_to_thickness_converter();
        var thickness = singlesToThickness.Convert(new object?[] { 2.0f, 4.0, 6.0f, 8.0 }, typeof(Thickness), null, CultureInfo.InvariantCulture);
        Require(thickness is Thickness t && t == new Thickness(2, 4, 6, 8), "singles_to_thickness_converter failed.");
        var pointToStr = new Point_to_String_converter();
        Require(Equals(pointToStr.Convert(new Point(1.5, 2.5), typeof(string), null, CultureInfo.InvariantCulture), "x1.500 y2.500"),
            "Point_to_String_converter failed.");
        var vecToStr = new Vector3D_to_string_converter();
        Require(Equals(vecToStr.Convert(new Vector3(1, 2, 3), typeof(string), null, CultureInfo.InvariantCulture), "1.000 2.000 3.000"),
            "Vector3D_to_string_converter failed.");

        // 6. Wave 2A: expandable_items_control & sdk_disclosure theme
        var expandable = new expandable_items_control
        {
            Header = "Material",
            ItemsSource = new[] { "Albedo", "Roughness" }
        };
        bool expandedChanged = false;
        expandable.expanded_changed += (_, _) => expandedChanged = true;
        var expWindow = new Window { Width = 300, Height = 200, Content = expandable };
        expWindow.Show();
        Dispatcher.UIThread.RunJobs();
        expandable.is_expanded = true;
        Dispatcher.UIThread.RunJobs();
        Require(expandedChanged && expandable.is_expanded, "expandable_items_control did not raise expanded_changed.");
        expWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 7. Wave 2A: ruler headless layout & render
        var rulerControl = new ruler
        {
            Width = 240,
            Height = 28,
            name = "Timeline",
            layout_scale = static () => 2.0f,
            layout_width = static () => 240.0f
        };
        var rulerWindow = new Window { Width = 260, Height = 60, Content = rulerControl };
        rulerWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Require(rulerControl.Bounds.Width > 0 && rulerControl.Bounds.Height > 0, "ruler failed layout.");
        rulerWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 8. Wave 2A: color_palette & color_picker.dialog
        var palette = new color_palette();
        bool paletteChanged = false;
        palette.selected_color_changed += (_, _) => paletteChanged = true;
        var paletteWindow = new Window { Width = 420, Height = 260, Content = palette };
        paletteWindow.Show();
        Dispatcher.UIThread.RunJobs();
        palette.selected_color = new color_hsv(120, 0.8, 0.9, 1.0);
        Dispatcher.UIThread.RunJobs();
        Require(paletteChanged, "color_palette did not raise selected_color_changed.");
        paletteWindow.Close();
        Dispatcher.UIThread.RunJobs();

        var colorDialog = new dialog { selected_color = new color_rgb(0.25, 0.5, 0.75, 1.0) };
        var dlgOk = colorDialog.FindControl<Button>("OKButton");
        Require(dlgOk != null, "color_picker.dialog missing OKButton.");
        colorDialog.Show();
        Dispatcher.UIThread.RunJobs();
        colorDialog.selected_color = new color_rgb(0.9, 0.2, 0.1, 1.0);
        Dispatcher.UIThread.RunJobs();
        Require(dlgOk!.IsEnabled, "color_picker.dialog OKButton should enable after color edit.");
        colorDialog.Close();
        Dispatcher.UIThread.RunJobs();

        // 9. Wave 2A: color_ramp
        var ramp = new color_ramp { Width = 220, Height = 28 };
        int addedCount = 0;
        int removedCount = 0;
        ramp.key_added += (_, _) => addedCount++;
        ramp.key_removed += (_, _) => removedCount++;
        var rampWindow = new Window { Width = 240, Height = 50, Content = ramp };
        rampWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var stop1 = ramp.add_key(0.0, new color_rgb(1, 0, 0, 1));
        var stop2 = ramp.add_key(0.5, new color_rgb(0, 1, 0, 1));
        Dispatcher.UIThread.RunJobs();
        Require(addedCount == 0 && ramp.gradient.GradientStops.Count == 2 && !ReferenceEquals(stop1, stop2), "color_ramp add_key failed.");
        ramp.remove_key(stop2);
        Dispatcher.UIThread.RunJobs();
        Require(removedCount == 1 && ramp.gradient.GradientStops.Count == 1, "color_ramp remove_key failed.");
        rampWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 10. Wave 2A: Skia SkSL bilinear shader & color_matrix_control
        Require(BilinearColorDrawOperation.CompileShader(out string? shaderError) && shaderError == null,
            $"BilinearColorDrawOperation SkSL shader failed to compile: {shaderError}");

        var matrix = new color_matrix(new Size(1.0, 1.0));
        matrix.add_row(new List<color_matrix_element>
        {
            new() { color = new color_rgb(0, 0, 0, 1), position = new Point(0, 0) },
            new() { color = new color_rgb(1, 0, 0, 1), position = new Point(1, 0) }
        });
        matrix.add_row(new List<color_matrix_element>
        {
            new() { color = new color_rgb(0, 1, 0, 1), position = new Point(0, 1) },
            new() { color = new color_rgb(1, 1, 1, 1), position = new Point(1, 1) }
        });

        int matrixEdits = 0;
        var matrixControl = new color_matrix_control
        {
            Width = 220,
            Height = 220,
            edited_color_matrix = matrix
        };
        matrix.edit_complete += (_, _) => matrixEdits++;
        var matrixWindow = new Window { Width = 240, Height = 240, Content = matrixControl };
        matrixWindow.Show();
        Dispatcher.UIThread.RunJobs();
        matrixControl.add_horizontal_at(0.5);
        matrixControl.add_vertical_at(0.5);
        Dispatcher.UIThread.RunJobs();
        Require(matrix.width == 3 && matrix.height == 3, "color_matrix add_horizontal/vertical did not grow to 3x3.");
        matrixControl.remove_horizontal(0.5);
        matrixControl.remove_vertical(0.5);
        Dispatcher.UIThread.RunJobs();
        Require(matrix.width == 2 && matrix.height == 2 && matrixEdits >= 4,
            "color_matrix remove_horizontal/vertical did not shrink back to 2x2.");
        matrixWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 11. Wave 2B: number_editor test
        int numChangedCount = 0;
        var numEditor = new number_editor
        {
            Width = 140,
            Height = 24,
            min_value = 0f,
            max_value = 10f,
            precision = 2,
            value = 2.5f
        };
        numEditor.value_changed += () => numChangedCount++;
        var numWindow = new Window { Width = 200, Height = 80, Content = numEditor };
        numWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var numTextBox = numEditor.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(tb => tb.Name == "PART_text_box");
        Require(numTextBox != null, "number_editor missing PART_text_box template child.");
        numTextBox!.Text = "not_a_number";
        Dispatcher.UIThread.RunJobs();
        Require(DataValidationErrors.GetHasErrors(numTextBox), "number_editor did not flag parse error.");
        numTextBox.Text = "99.0";
        Dispatcher.UIThread.RunJobs();
        Require(DataValidationErrors.GetHasErrors(numTextBox), "number_editor did not flag out-of-range error.");
        numTextBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();
        Require(!DataValidationErrors.GetHasErrors(numTextBox), "number_editor Escape did not revert validation error.");
        numEditor.value = 6.25f;
        Dispatcher.UIThread.RunJobs();
        Require(numChangedCount >= 1 && numTextBox.Text == 6.25f.ToString("F2", CultureInfo.CurrentCulture),
            "number_editor value update did not format text cleanly.");
        numWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 12. Wave 2B: property_container + property_extractor test
        string cbBacking = "hello";
        bool btnClicked = false;
        var subContainer = new property_container();
        subContainer.properties.add("SubFloat", "Nested", "Nested float", 1.5f, new callback_property_value<float>(() => 1.5f, _ => { }));
        var dynContainer = new property_container();
        dynContainer.properties.add("DynText", "Dynamic", "Dynamic text", "hello", new callback_property_value<string>(() => cbBacking, v => cbBacking = v));
        dynContainer.properties.add_container("SubBlock", "Dynamic", "Nested block", subContainer);
        var stackCtrl = dynContainer.add_stack_container();
        stackCtrl.category = "Actions";
        var actBtn = stackCtrl.add_button("Ping", _ => btnClicked = true);
        _ = actBtn;
        var togBtn = stackCtrl.add_toggle_button("Flag", _ => { });
        _ = togBtn;

        var dynExtracted = property_extractor.extract(dynContainer, null);
        Require(dynExtracted.Count == 3, $"Expected 3 extracted dynamic descriptors, got {dynExtracted.Count}.");
        Require(!btnClicked, "Unexpected button click before interaction.");

        // 13. Wave 2B: property_grid_control end-to-end test
        var modelA = new TestPropertyModel();
        var modelB = new TestPropertyModel { IntProp = 99, NameProp = "Beta", BoolProp = false };

        var pg = new property_grid_control { Width = 420, Height = 520 };
        int propChangedCount = 0;
        int refreshChangedCount = 0;
        object? lastOldValue = null;
        object? lastNewValue = null;
        string? lastChangedProp = null;
        string? lastStack = null;
        pg.property_value_changed += (_, args) =>
        {
            propChangedCount++;
            lastOldValue = args.old_value;
            lastNewValue = args.new_value;
            lastChangedProp = args.changed_property.name;
            lastStack = Environment.StackTrace;
        };
        pg.refresh_property_changed += (_, _) => refreshChangedCount++;

        var pgWindow = new Window { Width = 460, Height = 560, Content = pg };
        pgWindow.Show();
        Dispatcher.UIThread.RunJobs();

        pg.selected_object = modelA;
        Dispatcher.UIThread.RunJobs();
        Require(propChangedCount == 0,
            $"Unexpected property_value_changed during initial load ({propChangedCount}): '{lastChangedProp}' ({lastOldValue} -> {lastNewValue})\n{lastStack}");

        Require(pg.data_view.Count == 3, $"Expected 3 categories, got {pg.data_view.Count}.");
        Require(pg.grid_items.Count == 13, $"Expected 13 grid items, got {pg.grid_items.Count}.");

        var expectedEditors = new Dictionary<string, Type>
        {
            ["Enabled"] = typeof(Boolean_editor),
            ["Count"] = typeof(int32_editor),
            ["Intensity"] = typeof(single_editor),
            ["Scale"] = typeof(single_editor),
            ["Title"] = typeof(string_editor),
            ["Mode"] = typeof(Enum_editor),
            ["Offset"] = typeof(vector3_editor),
            ["Tint"] = typeof(color_editor),
            ["ColorCurve"] = typeof(color_curve_editor),
            ["ColorMatrix"] = typeof(xray.editor.avalonia_controls.property_editors.value.color_matrix_editor),
            ["FloatCurve"] = typeof(float_curve_editor),
            ["Preset"] = typeof(combo_box_editor),
            ["Indices"] = typeof(IList_collection_editor)
        };

        foreach (var item in pg.grid_items)
        {
            Require(item.Bounds.Width > 0 && item.Bounds.Height > 0, $"property_grid_item '{item.name}' failed layout.");
            Require(item.item_editor is named_item_editor, $"Unexpected item_editor for '{item.name}'.");
            var nie = (named_item_editor)item.item_editor!;
            Require(expectedEditors.TryGetValue(item.name, out var expType) && nie.value_editor?.GetType() == expType,
                $"Property '{item.name}' resolved {nie.value_editor?.GetType().Name} instead of {expectedEditors[item.name].Name}.");
        }

        // Mutation & refresh_grid_on_change check
        var countItem = pg.grid_items.First(i => i.name == "Count");
        var countProp = (xray.editor.avalonia_controls.property_grid.property)countItem.m_property!;
        countProp.value = 42;
        Dispatcher.UIThread.RunJobs();
        Require(modelA.IntProp == 42 && !countProp.is_default_value && propChangedCount == 1 && Equals(lastOldValue, 7) && refreshChangedCount == 1,
            $"property_grid mutation check failed: IntProp={modelA.IntProp}, is_default={countProp.is_default_value}, propChangedCount={propChangedCount} (last={lastChangedProp}), old={lastOldValue}, refresh={refreshChangedCount}.");

        // Expand hierarchy & remember_expanded_hierarchy check
        var indicesItem = pg.grid_items.First(i => i.name == "Indices");
        indicesItem.is_expanded = true;
        Dispatcher.UIThread.RunJobs();
        Require(indicesItem.Items.Count == 2, $"Expected 2 collection sub-items after expand, got {indicesItem.Items.Count}.");

        // Multi-object selection & set_properties_values check
        pg.selected_objects = new object[] { modelA, modelB };
        Dispatcher.UIThread.RunJobs();
        var sharedIntItem = pg.grid_items.First(i => i.name == "Count");
        var sharedIntProp = (xray.editor.avalonia_controls.property_grid.property)sharedIntItem.m_property!;
        Require(sharedIntProp.is_multiple_values, "Multi-object selection did not mark differing IntProp as is_multiple_values.");
        sharedIntProp.is_selected = true;
        pg.set_properties_values(123);
        Dispatcher.UIThread.RunJobs();
        Require(modelA.IntProp == 123 && modelB.IntProp == 123, "set_properties_values did not update all selected objects.");

        var rememberedIndices = pg.grid_items.First(i => i.name == "Indices");
        Require(rememberedIndices.is_expanded, "remember_expanded_hierarchy did not preserve Indices expansion.");

        // Step 14: float_curve_editor live curve_editor_panel inside PropertyGrid
        pg.selected_object = modelA;
        pg.read_only_mode = false;
        Dispatcher.UIThread.RunJobs();
        var fcItem = pg.grid_items.First(i => i.name == "FloatCurve");
        var fcEditor = (float_curve_editor)((named_item_editor)fcItem.item_editor!).value_editor!;
        Require(fcEditor.curve_panel != null, "float_curve_editor.curve_panel was null.");
        Require(fcEditor.curve_panel!.edited_curves.Count == 1, $"Expected 1 edited curve in float_curve_editor, got {fcEditor.curve_panel.edited_curves.Count}.");
        Require(fcEditor.curve_panel.Bounds.Width > 0 && fcEditor.curve_panel.Bounds.Height > 0, "float_curve_editor live curve_panel failed layout.");

        // Dynamic container & read-only mode check
        pg.selected_object = dynContainer;
        pg.read_only_mode = true;
        Dispatcher.UIThread.RunJobs();
        pg.update();
        pg.reset();
        Dispatcher.UIThread.RunJobs();

        pgWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 15: Wave 2C curve_editor_panel headless smoke
        var testCurve = new float_curve(new List<float_curve_key>
        {
            new(new Point(0, 0), float_curve_key_type.linear),
            new(new Point(1, 5), float_curve_key_type.locked_auto),
            new(new Point(2, 2), float_curve_key_type.step)
        }) { name = "Alpha", color = Colors.OrangeRed };
        var curveGroup = new float_curve_group("Wave2CGroup");
        curveGroup.add_curve(testCurve);
        curveGroup.effects.Add(new float_curve_noise_effect { m_seed = 42, m_strength = 1.5f, m_frequency = 0.5f });

        var cep = new curve_editor_panel { Width = 640, Height = 320 };
        float_curve? changedCurve = null;
        cep.float_curve_changed += c => changedCurve = c;
        cep.add_group(curveGroup);
        cep.add_custom_template(new float_curve(new List<float_curve_key>
        {
            new(new Point(0, 0)),
            new(new Point(2, 10))
        }) { name = "Ramp" });

        var cepWindow = new Window { Width = 660, Height = 360, Content = cep };
        cepWindow.Show();
        Dispatcher.UIThread.RunJobs();
        cep.focus_curves();
        cep.select_keys_by_index(1);
        Require(cep.panel_curves.Count == 1, $"Expected 1 panel curve, got {cep.panel_curves.Count}.");
        cep.raise_curves_change();
        Dispatcher.UIThread.RunJobs();
        Require(changedCurve == testCurve, "curve_editor_panel.float_curve_changed did not fire.");
        Require(cep.custom_templates.Count == 1, $"Expected 1 custom template, got {cep.custom_templates.Count}.");
        Require(cep.Bounds.Width > 0 && cep.Bounds.Height > 0, "curve_editor_panel failed layout.");
        cepWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 16: Wave 2C animation_curve_editor headless smoke
        var ace = new animation_curve_editor { Width = 680, Height = 320 };
        var aceGroup = ace.add_group("Camera");
        var posXCurve = new float_curve(new List<float_curve_key>
        {
            new(new Point(0, 0)),
            new(new Point(2, 10))
        }) { name = "position x", color = Colors.DeepSkyBlue };
        aceGroup.add_curve("position x", "position x", posXCurve);
        var aceWindow = new Window { Width = 700, Height = 360, Content = ace };
        aceWindow.Show();
        Dispatcher.UIThread.RunJobs();
        ace.current_time = 0.5;
        ace.fps = 60;
        ace.snap_to_frames = true;
        ace.play_speed = 2.0;
        Require(ace.current_time_text_frames == "30f", $"Expected '30f', got '{ace.current_time_text_frames}'.");
        Require(ace.current_time_text_seconds == "0.50s", $"Expected '0.50s', got '{ace.current_time_text_seconds}'.");
        ace.tick(0.25f);
        Require(ace.Bounds.Width > 0 && ace.Bounds.Height > 0, "animation_curve_editor failed layout.");
        aceWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 17: Wave 2C time_layout headless smoke
        var tl = new time_layout { Width = 640, Height = 220 };
        float totalLength = 12f;
        float start0 = 1f, len0 = 4f;
        float start1 = 2f, len1 = 5f;
        var tlItems = new List<time_layout_item>
        {
            new(tl, () => { }, "Emitter_A", v => start0 = v, () => start0, v => len0 = v, () => len0),
            new(tl, () => { }, "Action_B", v => start1 = v, () => start1, v => len1 = v, () => len1)
        };
        tl.set_new_time_layout_params("Root", v => totalLength = v, () => totalLength, tlItems);
        tl.time_layout_scale = 20f;
        var tlWindow = new Window { Width = 660, Height = 260, Content = tl };
        tlWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Require(Math.Abs(tl.parent_length_time - 12f) < 0.001f, $"Expected parent_length_time=12, got {tl.parent_length_time}.");
        Require(Math.Abs(tl.time_layout_scale - 20f) < 0.001f, $"Expected time_layout_scale=20, got {tl.time_layout_scale}.");
        var renderedItems = tl.items.GetVisualDescendants().OfType<time_layout_item_control>().Count();
        Require(renderedItems == 2, $"Expected 2 time_layout_item_control elements, got {renderedItems}.");
        Require(tl.Bounds.Width > 0 && tl.Bounds.Height > 0, "time_layout failed layout.");
        tlWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 18: Wave 2D hypergraph_control + NumericUpDown above-header content + field grid
        var hg = new hypergraph_control { Width = 720, Height = 420, is_grid_enabled = true };
        var nodeSrcA = new TestPropertyModel();
        var nodeSrcB = new TestPropertyModel();
        var pnA = hg.create_node("node_a", nodeSrcA, new Point(40, 40));
        pnA.name = "AlphaNode";
        var aboveHeader = new groups_editor_node_above_header_content();
        aboveHeader.up_down.Value = 3;
        pnA.above_header_content = aboveHeader;
        var pnB = hg.create_node("node_b", nodeSrcB, new Point(340, 120));
        pnB.name = "BetaNode";
        var hgLink = hg.create_link("node_a", "node_b", pnA.output_link_point.id, pnB.input_link_point.id);
        var hgWindow = new Window { Width = 740, Height = 440, Content = hg };
        hgWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Require(hg.nodes.Count == 2, $"Expected 2 hypergraph nodes, got {hg.nodes.Count}.");
        Require(hg.links.Count == 1, $"Expected 1 hypergraph link, got {hg.links.Count}.");
        Require(aboveHeader.up_down.Value == 3, "groups_editor_node_above_header_content NumericUpDown mismatch.");
        Require(hgLink.id.output_node_id == "node_a" && hgLink.id.input_node_id == "node_b", "Unexpected link endpoints.");
        Require(hg.Bounds.Width > 0 && hg.Bounds.Height > 0, "hypergraph_control failed layout.");
        hgWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 19: Wave 2D data_sources_panel headless smoke
        var dsp = new data_sources_panel { Width = 420, Height = 260 };
        dsp.data_sources.ItemsSource = new List<object> { new TestPropertyModel(), new TestPropertyModel() };
        var dspWindow = new Window { Width = 440, Height = 280, Content = dsp };
        dspWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Require(dsp.GetVisualDescendants().OfType<data_source_item>().Count() == 2, "Expected 2 data_source_item controls.");
        Require(dsp.Bounds.Width > 0 && dsp.Bounds.Height > 0, "data_sources_panel failed layout.");
        dspWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 20: Wave 2D logic_view + logic_scenes_view + logic_entity_view + event_control
        var lv = new logic_view { Width = 760, Height = 420 };
        var entityView = new logic_entity_view { Width = 680, Height = 180 };
        entityView.logic_hypergraph.create_node("logic_n1", new TestPropertyModel(), new Point(20, 20));
        lv.add_logic(entityView);
        var evCtrl = new event_control { event_name = "OnTrigger", logical_position = 120 };
        lv.add_event(evCtrl);
        var lvWindow = new Window { Width = 780, Height = 440, Content = lv };
        lvWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Require(lv.scenes_view != null, "logic_view.scenes_view should not be null.");
        Require(evCtrl.event_name == "OnTrigger", "event_control.event_name mismatch.");
        Require(entityView.logic_hypergraph.nodes.Count == 1, "logic_entity_view hypergraph missing node.");
        Require(lv.Bounds.Width > 0 && lv.Bounds.Height > 0, "logic_view failed layout.");
        lvWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 21: Wave 2D animation_playback_panel headless smoke
        var appPanel = new animation_playback_panel { Width = 680, Height = 280, max_time = 2000f };
        var pbItem = new xray.editor.avalonia_controls.animation_playback.animation_item
        {
            name = "walk_forward",
            length = 1200f,
            position = 100f
        };
        pbItem.events.Add(new animation_event(pbItem) { position = 400f, text = "step" });
        appPanel.animation_items = new[] { pbItem };
        appPanel.animation_time = 250f;
        var appWindow = new Window { Width = 700, Height = 300, Content = appPanel };
        appWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Require(appPanel.GetVisualDescendants().OfType<animation_item_view>().Count() == 1, "Expected 1 animation_item_view.");
        Require(appPanel.Bounds.Width > 0 && appPanel.Bounds.Height > 0, "animation_playback_panel failed layout.");
        appWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 22: Wave 2D animation_setup_panel headless smoke
        var asp = new animation_setup_panel { Width = 720, Height = 320, animation_length = 1000f, max_time = 1000f };
        var partCh = asp.add_channel("anim_intervals", animation_channel_type.partitions);
        var intCh = asp.add_channel("intervals", animation_channel_type.intervals);
        var evtCh = asp.add_channel("sound_events", animation_channel_type.events);
        asp.add_item("intervals", 100f, Guid.Empty);
        asp.add_item("sound_events", 250f, Guid.Empty);
        var aspWindow = new Window { Width = 740, Height = 340, Content = asp };
        aspWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Require(asp.channels.Count == 3, $"Expected 3 setup channels, got {asp.channels.Count}.");
        Require(partCh.objects.Count >= 1 && intCh.objects.Count == 1 && evtCh.objects.Count == 1, "animation_setup_panel channel items mismatch.");
        Require(asp.GetVisualDescendants().OfType<animation_channel_list_view>().Count() == 3, "Expected 3 animation_channel_list_view controls.");
        Require(asp.Bounds.Width > 0 && asp.Bounds.Height > 0, "animation_setup_panel failed layout.");
        aspWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 23: Wave 2D animation_lexeme_panel headless smoke
        var alp = new animation_lexeme_panel { Width = 720, Height = 320 };
        var lexAnim = new xray.editor.avalonia_controls.animation_lexeme_panel.animation_item("anims/run_cycle", 1000f) { panel = alp };
        var lexInterval = new animation_interval_item(lexAnim, 0f, 400f, 0, animation_channel_partition_type.both);
        alp.animations.Add(lexAnim);
        alp.lexeme.insert_interval(lexInterval, 0);
        var alpWindow = new Window { Width = 740, Height = 340, Content = alp };
        alpWindow.Show();
        Dispatcher.UIThread.RunJobs();
        Require(lexAnim.short_name == "run_cycle", $"Expected short_name 'run_cycle', got '{lexAnim.short_name}'.");
        Require(alp.lexeme.intervals.Count == 1, $"Expected 1 lexeme interval, got {alp.lexeme.intervals.Count}.");
        Require(alp.GetVisualDescendants().OfType<animation_lexeme_item_view>().Count() == 1, "Expected 1 animation_lexeme_item_view.");
        Require(alp.Bounds.Width > 0 && alp.Bounds.Height > 0, "animation_lexeme_panel failed layout.");
        alpWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 24: UI-3 AvaloniaViewportHost lease and parking invariant with mock backend
        var recBackend = new RecordingSurfaceBackend();
        var recHost = new AvaloniaViewportHost(recBackend, 640, 480);
        recHost.AcquireSurfaceLease();
        nint leasedHandle = recHost.SurfaceHandle;
        Require(leasedHandle == recBackend.SurfaceHandle, "Expected mock surface handle.");
        Require(recHost.SurfaceLeased && recHost.HasSurface && recHost.IsParked, "Expected leased parked surface before attach.");

        bool doubleAcquireThrew = false;
        try { recHost.AcquireSurfaceLease(); } catch (InvalidOperationException) { doubleAcquireThrew = true; }
        Require(doubleAcquireThrew, "Double AcquireSurfaceLease must throw.");

        var parentA = new PlatformHandle(0xAAA1, "HWND");
        var attachedA = recHost.AttachToParentForTest(parentA);
        Require(attachedA.Handle == leasedHandle && recHost.AttachedParentHandle == parentA.Handle && !recHost.IsParked,
            "AttachToParentForTest did not reparent surface to parent A.");

        recHost.DetachFromParentForTest(attachedA);
        Require(recHost.IsParked && recHost.HasSurface && !recBackend.SurfaceDestroyed && !recBackend.ParkingDestroyed,
            "DestroyNativeControlCore must park, never destroy, a leased surface.");

        var parentB = new PlatformHandle(0xBBB2, "HWND");
        var attachedB = recHost.AttachToParentForTest(parentB);
        Require(attachedB.Handle == leasedHandle && recHost.AttachedParentHandle == parentB.Handle,
            "Re-attaching to parent B must reuse the same leased SurfaceHandle.");

        recHost.Dispose();
        Require(recHost.IsDisposed && !recBackend.SurfaceDestroyed && recHost.HasSurface,
            "Dispose while SurfaceLeased must defer native surface destruction.");

        recHost.ReleaseSurfaceLease();
        Require(!recHost.SurfaceLeased && !recHost.HasSurface && recBackend.SurfaceDestroyed && recBackend.ParkingDestroyed,
            "ReleaseSurfaceLease after deferred dispose must destroy both surface and parking windows.");

        bool releaseWithoutLeaseThrew = false;
        var unleasedHost = new AvaloniaViewportHost(new RecordingSurfaceBackend());
        try { unleasedHost.ReleaseSurfaceLease(); } catch (InvalidOperationException) { releaseWithoutLeaseThrew = true; }
        Require(releaseWithoutLeaseThrew, "ReleaseSurfaceLease without lease must throw.");
        unleasedHost.Dispose();

        bool handleAfterDisposeThrew = false;
        try { _ = recHost.SurfaceHandle; } catch (ObjectDisposedException) { handleAfterDisposeThrew = true; }
        Require(handleAfterDisposeThrew, "SurfaceHandle after Dispose must throw ObjectDisposedException.");

        // Step 25: UI-3 DPI-invariant coordinate scaling (100% - 200%) and ForceResizePixels
        var dpiBackend = new RecordingSurfaceBackend();
        var dpiHost = new AvaloniaViewportHost(dpiBackend, 320, 240);
        dpiHost.AcquireSurfaceLease();
        dpiHost.ForceResizePixels(1280, 720);
        Require(dpiHost.SurfacePixelWidth == 1280 && dpiHost.SurfacePixelHeight == 720 && dpiBackend.LastWidth == 1280 && dpiBackend.LastHeight == 720,
            "ForceResizePixels did not propagate to backend.");
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var pxSize = dpiHost.DipToPhysicalSize(new Size(400, 200), scale);
            Require(pxSize.Width == (int)Math.Round(400 * scale) && pxSize.Height == (int)Math.Round(200 * scale),
                $"Unexpected physical size at scale {scale}.");
            var ray = dpiHost.DipToCenteredRayPixels(new Point(200, 100), new Size(400, 200), scale);
            Require(MathF.Abs(ray.X) < 0.01f && MathF.Abs(ray.Y) < 0.01f, $"Center ray must stay (0,0) at scale {scale}.");
        }
        dpiHost.Dispose();
        dpiHost.ReleaseSurfaceLease();

        // Step 26: UI-3 real Win32 HWND lifecycle via Win32NativeSurfaceHostBackend
        if (OperatingSystem.IsWindows())
        {
            var win32Host = new AvaloniaViewportHost(new Win32NativeSurfaceHostBackend(), 320, 240);
            win32Host.AcquireSurfaceLease();
            nint realHwnd = win32Host.SurfaceHandle;
            Require(Win32NativeSurfaceHostBackend.IsWindowValid(realHwnd), "Expected valid Win32 child HWND.");
            win32Host.ForceResizePixels(800, 600);
            win32Host.DetachFromParentForTest(new PlatformHandle(realHwnd, "HWND"));
            win32Host.Dispose();
            Require(Win32NativeSurfaceHostBackend.IsWindowValid(realHwnd), "Win32 HWND must remain valid while SurfaceLeased.");
            win32Host.ReleaseSurfaceLease();
            Require(!Win32NativeSurfaceHostBackend.IsWindowValid(realHwnd), "Win32 HWND must be destroyed after ReleaseSurfaceLease.");
        }

        // Step 27: UI-4 AvaloniaImageAtlas cropping from images16x16.png
        var atlas = AvaloniaImageAtlas.Shared;
        Require(atlas.HasAtlasBitmap, "Expected images16x16.png atlas to load from avares://.");
        var folderIcon = atlas.GetIcon(images16x16.folder_closed);
        var toolIcon = atlas.GetIcon(images16x16.tool_translate);
        var fallbackIcon = atlas.GetIcon(9999);
        Require(folderIcon.Size.Width == 16 && folderIcon.Size.Height == 16, "Expected 16x16 cropped folder icon.");
        Require(toolIcon.Size.Width == 16 && toolIcon.Size.Height == 16, "Expected 16x16 cropped translate icon.");
        Require(fallbackIcon.Size.Width == 16 && fallbackIcon.Size.Height == 16, "Expected 16x16 fallback icon.");

        // Step 28: UI-4 SdkDockFactory + SdkDockLayoutSerializer JSON round-trip, limits, background float guard & rollback
        var recDockBackend = new RecordingSurfaceBackend();
        var dockViewport = new AvaloniaViewportHost(recDockBackend, 800, 600);
        dockViewport.AcquireSurfaceLease();
        nint preservedHandle = dockViewport.SurfaceHandle;
        var dockFactory = new SdkDockFactory(backgroundMode: false);
        dockFactory.SetPanelContent(SdkDockPanelIds.SceneView, dockViewport);
        var rootLayout = dockFactory.CreateDefaultWorkspaceLayout();
        byte[] layoutJson = SdkDockLayoutSerializer.Serialize(rootLayout);
        Require(layoutJson.Length > 0 && layoutJson.Length < SdkDockLayoutSerializer.MaxLayoutBytes, "Unexpected serialized Dock.Avalonia layout size.");

        var restoredRoot = SdkDockLayoutSerializer.ValidateAndDeserialize(
            layoutJson, dockFactory, backgroundMode: false, SdkDockPanelIds.IdeRequiredPanelIds, strictUnknownPanels: true);
        Require(dockFactory.FindDockableById(restoredRoot, SdkDockPanelIds.SceneView) != null, "Missing SceneView after JSON restore.");
        Require(dockFactory.FindDockableById(restoredRoot, SdkDockPanelIds.ObjectInspector) != null, "Missing ObjectInspector after JSON restore.");
        Require(ReferenceEquals(dockFactory.GetPanelContent(SdkDockPanelIds.SceneView), dockViewport) && dockViewport.SurfaceHandle == preservedHandle,
            "Layout restore must preserve live AvaloniaViewportHost and its SurfaceHandle.");

        bool oversizedThrew = false;
        try
        {
            SdkDockLayoutSerializer.ValidateAndDeserialize(new byte[SdkDockLayoutSerializer.MaxLayoutBytes + 1], dockFactory, false, SdkDockPanelIds.IdeRequiredPanelIds);
        }
        catch (InvalidDataException) { oversizedThrew = true; }
        Require(oversizedThrew, "Layout > 1 MiB must be rejected.");

        bool rollbackInvalidThrew = false;
        try
        {
            SdkDockLayoutSerializer.RestoreWithRollback(restoredRoot, Encoding.UTF8.GetBytes("{invalid json"), dockFactory, false, SdkDockPanelIds.IdeRequiredPanelIds);
        }
        catch { rollbackInvalidThrew = true; }
        Require(rollbackInvalidThrew && dockFactory.FindDockableById(restoredRoot, SdkDockPanelIds.SceneView) != null,
            "RestoreWithRollback must rethrow and keep previous layout intact.");

        var fw = dockFactory.CreateDockWindow();
        fw.Id = "Float1";
        restoredRoot.Windows = dockFactory.CreateList<IDockWindow>(fw);
        byte[] floatJson = SdkDockLayoutSerializer.Serialize(restoredRoot);
        bool floatBgRejected = false;
        try
        {
            SdkDockLayoutSerializer.ValidateAndDeserialize(floatJson, dockFactory, backgroundMode: true, SdkDockPanelIds.IdeRequiredPanelIds);
        }
        catch (InvalidDataException) { floatBgRejected = true; }
        Require(floatBgRejected, "Floating layout in backgroundMode must be rejected.");
        dockViewport.Dispose();
        dockViewport.ReleaseSurfaceLease();

        // Step 29: UI-4 legacy DockPanelSuite XML -> Dock.Avalonia JSON migration
        const string legacyXml = """
            <DockPanel FormatVersion="1.0">
              <Contents Count="4">
                <Content ID="0" PersistString="xray.editor.window_view" IsHidden="False" />
                <Content ID="1" PersistString="xray.editor.project_tab" IsHidden="False" />
                <Content ID="2" PersistString="xray.editor.controls.item_properties_panel_base" IsHidden="False" />
                <Content ID="3" PersistString="xray.editor.tools_tab" IsHidden="False" />
              </Contents>
              <DockWindows Count="3">
                <DockWindow DockState="Document"><NestedPanes Count="1"><Pane DockState="Document"><Contents Count="1"><Content RefID="0" /></Contents></Pane></NestedPanes></DockWindow>
                <DockWindow DockState="DockLeft"><NestedPanes Count="1"><Pane DockState="DockLeft"><Contents Count="2"><Content RefID="1" /><Content RefID="2" /></Contents></Pane></NestedPanes></DockWindow>
                <DockWindow DockState="DockRight"><NestedPanes Count="1"><Pane DockState="DockRight"><Contents Count="1"><Content RefID="3" /></Contents></Pane></NestedPanes></DockWindow>
              </DockWindows>
            </DockPanel>
            """;
        var xmlFactory = new SdkDockFactory(backgroundMode: true);
        var migratedRoot = SdkDockLayoutSerializer.ConvertLegacyDockPanelXml(
            Encoding.UTF8.GetBytes(legacyXml), xmlFactory, backgroundMode: true, SdkDockPanelIds.IdeRequiredPanelIds, strictUnknownPanels: true);
        Require(xmlFactory.FindDockableById(migratedRoot, SdkDockPanelIds.SceneView) != null, "Legacy alias xray.editor.window_view must normalize to SceneView.");
        byte[] migratedJson = SdkDockLayoutSerializer.Serialize(migratedRoot);
        Require(Encoding.UTF8.GetString(migratedJson).Contains(SdkDockPanelIds.SceneView, StringComparison.Ordinal), "Migrated JSON missing canonical SceneView ID.");

        // Real DockPanelSuite.SaveAsXml schema: top-level <Panes>, <NestedPanes><Pane RefID="..."/></NestedPanes>,
        // legacy xray.editor.object_inspector_tab, and empty Float DockWindow/FloatWindows.
        const string realDockPanelSuiteXml = """
            <DockPanel FormatVersion="1.0" DockLeftPortion="0.24" DockRightPortion="0.22">
              <Contents Count="4">
                <Content ID="0" PersistString="xray.editor.window_view" IsHidden="False" />
                <Content ID="1" PersistString="xray.editor.project_tab" IsHidden="False" />
                <Content ID="2" PersistString="xray.editor.object_inspector_tab" IsHidden="False" />
                <Content ID="3" PersistString="xray.editor.tools_tab" IsHidden="False" />
              </Contents>
              <Panes Count="3">
                <Pane ID="0" DockState="Document" ActiveContent="0">
                  <Contents Count="1"><Content ID="0" RefID="0" /></Contents>
                </Pane>
                <Pane ID="1" DockState="DockLeft" ActiveContent="1">
                  <Contents Count="2"><Content ID="0" RefID="1" /><Content ID="1" RefID="2" /></Contents>
                </Pane>
                <Pane ID="2" DockState="DockRight" ActiveContent="3">
                  <Contents Count="1"><Content ID="0" RefID="3" /></Contents>
                </Pane>
              </Panes>
              <DockWindows Count="5">
                <DockWindow ID="0" DockState="Document"><NestedPanes Count="1"><Pane ID="0" RefID="0" PrevPane="-1" Alignment="Right" Proportion="0.5" /></NestedPanes></DockWindow>
                <DockWindow ID="1" DockState="DockLeft"><NestedPanes Count="1"><Pane ID="0" RefID="1" PrevPane="-1" Alignment="Bottom" Proportion="0.5" /></NestedPanes></DockWindow>
                <DockWindow ID="2" DockState="DockRight"><NestedPanes Count="1"><Pane ID="0" RefID="2" PrevPane="-1" Alignment="Bottom" Proportion="0.5" /></NestedPanes></DockWindow>
                <DockWindow ID="3" DockState="DockTop"><NestedPanes Count="0" /></DockWindow>
                <DockWindow ID="4" DockState="Float"><NestedPanes Count="0" /></DockWindow>
              </DockWindows>
              <FloatWindows Count="0" />
            </DockPanel>
            """;
        var realXmlFactory = new SdkDockFactory(backgroundMode: true);
        var realMigratedRoot = SdkDockLayoutSerializer.ConvertLegacyDockPanelXml(
            Encoding.UTF8.GetBytes(realDockPanelSuiteXml), realXmlFactory, backgroundMode: true, SdkDockPanelIds.IdeRequiredPanelIds, strictUnknownPanels: true);
        Require(realXmlFactory.FindDockableById(realMigratedRoot, SdkDockPanelIds.SceneView) != null,
            "Real DockPanelSuite XML must map xray.editor.window_view -> SceneView.");
        Require(realXmlFactory.FindDockableById(realMigratedRoot, SdkDockPanelIds.ObjectInspector) != null,
            "Real DockPanelSuite XML must normalize xray.editor.object_inspector_tab -> ObjectInspector.");
        byte[] realMigratedJson = SdkDockLayoutSerializer.Serialize(realMigratedRoot);
        string realMigratedText = Encoding.UTF8.GetString(realMigratedJson);
        Require(realMigratedText.Contains(SdkDockPanelIds.ObjectInspector, StringComparison.Ordinal) &&
                realMigratedText.Contains(SdkDockPanelIds.SceneView, StringComparison.Ordinal),
            "Migrated JSON from real DockPanelSuite XML must contain canonical IDs.");

        // Step 30: UI-4 SdkAvaloniaShellWindow + AvaloniaGuiBinder + DockControl headless render & commands
        bool toggleChecked = false;
        int undoInvokeCount = 0;
        var shell = new SdkAvaloniaShellWindow(backgroundMode: false);
        var shellHost = new AvaloniaViewportHost(new RecordingSurfaceBackend(), 640, 480);
        shellHost.AcquireSurfaceLease();
        var shellInspector = new xray.editor.avalonia_controls.property_grid.property_grid_control();
        shell.SetPanelContent(SdkDockPanelIds.SceneView, shellHost);
        shell.SetPanelContent(SdkDockPanelIds.ObjectInspector, shellInspector);
        shell.GuiBinder.BindAction("Undo", () => ++undoInvokeCount, canExecute: () => true, shortcut: "Control+Z", icon: images16x16.tool_translate);
        shell.GuiBinder.BindAction("Snap", () => toggleChecked = !toggleChecked, canExecute: () => true, checkedState: () => toggleChecked ? 1 : 0, icon: images16x16.toggle_draw_grid);
        var undoMenuItem = shell.GuiBinder.AddMenuItem(shell.MainMenu, "Edit", "Undo", "Undo", 1, shell);
        var snapBtn = (ToggleButton)shell.GuiBinder.AddToolbarButton(shell.MainToolbar, "Snap", "Grid Snap", isToggle: true);
        shell.SetStatus("UI-4 Shell Active");
        shell.SetCameraStatus("Camera: 1.0, 2.0, -3.0");
        shell.SetGridStatus("Grid: 64 x 64");
        shell.SetErrorCount(2);

        shell.Show();
        Dispatcher.UIThread.RunJobs();
        undoMenuItem.Command!.Execute(null);
        snapBtn.Command!.Execute(null);
        shell.GuiBinder.UpdateItems();
        Require(undoInvokeCount == 1 && toggleChecked && snapBtn.IsChecked == true, "AvaloniaGuiBinder command/toggle execution failed.");
        Require(shell.ErrorCount == 2 && shell.Bounds.Width > 0 && shell.Bounds.Height > 0, "SdkAvaloniaShellWindow failed layout or status check.");
        Require(ReferenceEquals(TopLevel.GetTopLevel(shellHost), shell) && shellHost.Bounds.Width > 0 && shellHost.Bounds.Height > 0,
            "SdkAvaloniaShellWindow did not mount SceneView viewport host into the visual tree.");
        Require(ReferenceEquals(TopLevel.GetTopLevel(shellInspector), shell) && shellInspector.Bounds.Width > 0 && shellInspector.Bounds.Height > 0,
            "SdkAvaloniaShellWindow did not mount ObjectInspector control into the visual tree.");
        nint shellSurfaceBeforeLoad = shellHost.SurfaceHandle;
        byte[] shellLayoutJson = shell.SaveLayoutJson();
        shell.LoadLayoutJson(shellLayoutJson, SdkDockPanelIds.IdeRequiredPanelIds);
        Dispatcher.UIThread.RunJobs();
        Require(shellHost.SurfaceHandle == shellSurfaceBeforeLoad && ReferenceEquals(TopLevel.GetTopLevel(shellHost), shell) &&
                shellHost.Bounds.Width > 0 && shellHost.Bounds.Height > 0,
            "LoadLayoutJson lost SceneView viewport host mount or recreated SurfaceHandle.");
        Require(ReferenceEquals(TopLevel.GetTopLevel(shellInspector), shell) && shellInspector.Bounds.Width > 0 && shellInspector.Bounds.Height > 0,
            "LoadLayoutJson lost ObjectInspector visual tree mount.");
        shell.Close();
        Dispatcher.UIThread.RunJobs();
        shellHost.Dispose();
        shellHost.ReleaseSurfaceLease();

        // Step 31: UI-5 Wave 5A SdkTreeView + SdkDocumentBase / SdkDocumentEditorView / base tool panels
        var treeWindow = new Window { Width = 500, Height = 600 };
        var tree = new SdkTreeView { FilterVisible = true, AutoExpandOnFilter = true };
        treeWindow.Content = tree;
        treeWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var propsGroup = tree.AddGroup("props", (int)images16x16.folder_closed, (int)images16x16.folder_open)!;
        var woodboxNode = tree.AddItem("props/woodbox_mil", (int)images16x16.node_mesh);
        var barrelNode = tree.AddItem("props/barrel_fuel", (int)images16x16.node_mesh);
        var lightsGroup = tree.AddGroup("lights", (int)images16x16.folder_closed, (int)images16x16.folder_open)!;
        var sunLightNode = tree.AddItem("lights/sun_light", (int)images16x16.object_light);
        var pointLightNode = tree.AddItem("lights/point_light", (int)images16x16.object_light);
        woodboxNode.IsIgnored = true;
        Dispatcher.UIThread.RunJobs();

        Require(ReferenceEquals(tree.GetNode("props/woodbox_mil"), woodboxNode), "SdkTreeView.GetNode failed.");
        Require(woodboxNode.FullPath == "props/woodbox_mil", "SdkTreeNode.FullPath mismatch.");
        Require(woodboxNode.HasIcon && woodboxNode.CurrentIcon != null, "SdkTreeNode icon failed.");
        Require(woodboxNode.LabelDecorations == TextDecorations.Strikethrough, "SdkTreeNode IsIgnored strikethrough failed.");

        tree.TrackActiveNodes(new[] { "props/woodbox_mil", "lights/sun_light" });
        Require(tree.SelectedNodes.Count == 2, "SdkTreeView multiselect TrackActiveNodes failed.");
        tree.SelectAll();
        Require(tree.SelectedNodes.Count == 6, "SdkTreeView.SelectAll count mismatch.");
        tree.DeselectAll();
        Require(tree.SelectedNodes.Count == 0, "SdkTreeView.DeselectAll failed.");

        tree.FilterText = "sun";
        Dispatcher.UIThread.RunJobs();
        Require(!propsGroup.IsVisible && !woodboxNode.IsVisible && !barrelNode.IsVisible &&
                lightsGroup.IsVisible && sunLightNode.IsVisible && !pointLightNode.IsVisible,
            "SdkTreeView non-destructive filter failed.");
        tree.FilterText = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Require(propsGroup.IsVisible && woodboxNode.IsVisible && pointLightNode.IsVisible,
            "SdkTreeView filter reset failed.");

        tree.SearchVisible = true;
        tree.ExecuteSearch("light");
        Dispatcher.UIThread.RunJobs();
        Require(tree.SearchMatchCount >= 2 && sunLightNode.IsSearchMatch && pointLightNode.IsSearchMatch,
            "SdkTreeView search match highlighting failed.");
        int prevSearchIdx = tree.CurrentSearchMatchIndex;
        tree.SelectNextMatch();
        Require(tree.CurrentSearchMatchIndex == prevSearchIdx + 1, "SdkTreeView.SelectNextMatch failed.");
        tree.SelectPreviousMatch();
        Require(tree.CurrentSearchMatchIndex == prevSearchIdx, "SdkTreeView.SelectPreviousMatch failed.");
        tree.CloseSearch();
        Require(!sunLightNode.IsSearchMatch && !sunLightNode.IsCurrentSearchMatch && !tree.SearchVisible,
            "SdkTreeView.CloseSearch failed to clear highlights.");
        treeWindow.Close();
        Dispatcher.UIThread.RunJobs();

        var docEditorWindow = new Window { Width = 960, Height = 640 };
        var docEditor = new SdkDocumentEditorView("Wave5ATestEditor", createBasePanels: true, backgroundMode: true);
        docEditor.ViewPanel.TreeView.AddItem("scenes/doc_a.dlg", (int)images16x16.node_sound_file);
        docEditor.ViewPanel.TreeView.AddItem("scenes/doc_b.dlg", (int)images16x16.node_sound_file);
        int toolbarClicked = 0;
        docEditor.ToolbarPanel.AddButton("Bake", () => ++toolbarClicked, images16x16.tool_translate);
        docEditorWindow.Content = docEditor;
        docEditorWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var docA = docEditor.LoadDocument("scenes/doc_a.dlg")!;
        var docB = docEditor.LoadDocument("scenes/doc_b.dlg")!;
        Dispatcher.UIThread.RunJobs();
        Require(docEditor.OpenedDocuments.Count == 2 && ReferenceEquals(docEditor.ActiveDocument, docB),
            "SdkDocumentEditorView LoadDocument failed.");
        docEditor.PropertiesPanel.ShowProperties(new TestPropertyModel());
        Require(docEditor.PropertiesPanel.PropertyGridControl.data_view.Count > 0,
            "SdkItemPropertiesPanel direct Avalonia property_grid_control binding failed.");

        docA.IsSaved = false;
        Require(docA.Title!.EndsWith(" *", StringComparison.Ordinal), "SdkDocumentBase dirty suffix missing.");
        docA.SavePromptOverride = _ => SdkDocumentSaveChoice.Cancel;
        Require(!docEditor.TryCloseDocument(docA) && docEditor.OpenedDocuments.Count == 2,
            "SdkDocumentBase Cancel save prompt did not abort close.");
        docA.SavePromptOverride = _ => SdkDocumentSaveChoice.Yes;
        Require(docEditor.TryCloseDocument(docA) && docA.IsSaved && docEditor.OpenedDocuments.Count == 1,
            "SdkDocumentBase Yes save prompt did not save and close.");

        docEditor.IsSingleDocument = true;
        docEditor.IsReuseSingleDocument = true;
        var docC = docEditor.LoadDocument("scenes/doc_c.dlg")!;
        Require(docEditor.OpenedDocuments.Count == 1 && ReferenceEquals(docC, docB) && docC.Name == "scenes/doc_c.dlg",
            "SdkDocumentEditorView single-document reuse failed.");

        bool hiddenProps = !docEditor.ToggleToolPanel(SdkItemPropertiesPanel.PersistId);
        Dispatcher.UIThread.RunJobs();
        Require(hiddenProps && !docEditor.IsToolPanelVisible(SdkItemPropertiesPanel.PersistId),
            "SdkDocumentEditorView ToggleToolPanel hide failed.");
        bool restoredProps = docEditor.ToggleToolPanel(SdkItemPropertiesPanel.PersistId);
        Dispatcher.UIThread.RunJobs();
        Require(restoredProps && docEditor.IsToolPanelVisible(SdkItemPropertiesPanel.PersistId),
            "SdkDocumentEditorView ToggleToolPanel restore failed.");
        docEditorWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 32: UI-5 Wave 5B all 8 core IDE dock panels
        var panelBackend = new RecordingSurfaceBackend();
        var sceneVpHost = new AvaloniaViewportHost(panelBackend, 800, 600);
        sceneVpHost.AcquireSurfaceLease();
        var scenePanel = new SdkSceneViewPanel(sceneVpHost, showCameraLightActions: true);
        Require(SdkSceneViewPanel.ViewModeNames.Length == 21 && scenePanel.RenderViewModeName == "lit",
            "SdkSceneViewPanel 21 view modes check failed.");
        scenePanel.SetRenderViewMode("wireframe");
        Require(scenePanel.RenderViewModeIndex == 0 && scenePanel.RenderViewModeName == "wireframe",
            "SdkSceneViewPanel.SetRenderViewMode failed.");
        scenePanel.RenderModeSupported = idx => idx != 1;
        scenePanel.CycleRenderViewMode();
        Require(scenePanel.RenderViewModeIndex == 2 && scenePanel.RenderViewModeName == "unlit",
            "SdkSceneViewPanel.CycleRenderViewMode did not skip unsupported mode.");
        Require(scenePanel.BuildRenderModesContextMenu().Items.Count == 21,
            "SdkSceneViewPanel.BuildRenderModesContextMenu count mismatch.");

        scenePanel.GetMouseRayFromNormalizedViewportPoint(0f, 0f, out Vector3 rayOrigin, out Vector3 rayDir);
        Require(MathF.Abs(rayDir.Length() - 1.0f) < 1e-4f && rayDir.Z > 0.99f && MathF.Abs(rayOrigin.Z + 10f) < 1e-4f,
            "SdkSceneViewPanel centered screen ray mismatch.");

        bool rejectedNonFiniteCamera = false;
        try { scenePanel.SetCameraWorld(new Matrix4x4(float.NaN, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)); }
        catch (ArgumentException) { rejectedNonFiniteCamera = true; }
        Require(rejectedNonFiniteCamera, "SdkSceneViewPanel.SetCameraWorld must reject NaN.");

        var savedSceneSettings = new Dictionary<string, string>(StringComparer.Ordinal);
        scenePanel.RenderGrid = true;
        scenePanel.NearPlane = 0.25f;
        scenePanel.GridDim = 64;
        scenePanel.SaveSettings(savedSceneSettings);
        var scenePanel2 = new SdkSceneViewPanel();
        scenePanel2.LoadSettings(savedSceneSettings);
        Require(scenePanel2.RenderGrid && MathF.Abs(scenePanel2.NearPlane - 0.25f) < 1e-4f && scenePanel2.GridDim == 64 && scenePanel2.RenderViewModeName == "unlit",
            "SdkSceneViewPanel SaveSettings/LoadSettings round-trip failed.");

        var inspectorPanel = new SdkObjectInspectorPanel();
        inspectorPanel.ShowProperties(new TestPropertyModel());
        Require(inspectorPanel.PropertyGridControl.data_view.Count > 0, "SdkObjectInspectorPanel failed to bind property grid.");
        VerifyWorkspaceInspectorRange();

        var projectPanel = new SdkProjectPanel();
        projectPanel.Populate(new SdkProjectTreeEntry(
            "default_scene",
            "default_scene",
            IsFolder: true,
            IsLocked: false,
            IsIgnored: false,
            ToolName: string.Empty,
            Tag: null,
            Children: new[]
            {
                new SdkProjectTreeEntry("lights", "default_scene/lights", true, true, false, string.Empty, null, new[]
                {
                    new SdkProjectTreeEntry("sun", "default_scene/lights/sun", false, false, false, "light", null)
                }),
                new SdkProjectTreeEntry("woodbox", "default_scene/woodbox", false, false, true, "solid visual", null)
            }));
        projectPanel.SetCounts(2, 1);
        projectPanel.TrackSelection(new[] { "default_scene/lights/sun" });
        VerifyProjectTreeInteraction();
        Require(projectPanel.TotalLabelText == "Total:2" && projectPanel.SelectedLabelText == "Selected:1" &&
                projectPanel.TreeView.SelectedNodes.Count == 1 &&
                projectPanel.TreeView.GetNode("default_scene/woodbox")!.IsIgnored,
            "SdkProjectPanel hierarchy/counts/ignored check failed.");

        var toolsPanel = new SdkToolsPanel();
        string? lastActivatedTool = null;
        toolsPanel.ActiveToolChanged += t => lastActivatedTool = t;
        toolsPanel.ActivateTab(1);
        Require(toolsPanel.ActiveToolCount == 9 && toolsPanel.ActiveToolName == "lights" && lastActivatedTool == "lights",
            "SdkToolsPanel 9 active tool tabs check failed.");
        var lightsTab = toolsPanel.ToolTabs["lights"];
        lightsTab.LibraryTree.TrackActiveNode("lights/sun_light");
        Require(lightsTab.StatusText == "lights: lights/sun_light", "SdkToolLibraryTabView status selection check failed.");
        VerifyToolLibraryActivation();

        var sceneGraphPanel = new SdkSceneGraphPanel();
        sceneGraphPanel.PopulateNodes(new[]
        {
            new SdkSceneGraphEntry("node_sun", "Editable object", new { Intensity = 1.5f }),
            new SdkSceneGraphEntry("node_box", "Editable object", new { Static = true })
        });
        Require(sceneGraphPanel.HypergraphControl.nodes.Values.OfType<properties_node>().Count(node => node.name == "Editable object") == 2,
            "Scene graph could not display objects with duplicate names under separate identities.");

        var camBackend = new RecordingSurfaceBackend();
        var camHost = new AvaloniaViewportHost(camBackend, 320, 240);
        camHost.AcquireSurfaceLease();
        var cameraPanel = new SdkCameraViewPanel(camHost);
        Require(ReferenceEquals(cameraPanel.ViewportHost, camHost), "SdkCameraViewPanel host mismatch.");

        var animCurvesPanel = new SdkAnimationCurveEditorPanel();
        var wave5bCurveGroup = new float_curve_group("Wave5BCurve");
        wave5bCurveGroup.add_curve(new float_curve(new List<float_curve_key>
        {
            new(new Point(0, 0)),
            new(new Point(1, 2))
        }) { name = "Speed" });
        animCurvesPanel.CurveEditorControl.add_group(wave5bCurveGroup);
        Require(animCurvesPanel.CurveEditorControl.panel_curves.Count == 1,
            "SdkAnimationCurveEditorPanel curve group add failed.");

        var triPanel = new SdkTriangulationTesterPanel();
        triPanel.AddVertex(new Point(10, 10));
        triPanel.AddVertex(new Point(110, 10));
        triPanel.AddVertex(new Point(110, 110));
        triPanel.AddVertex(new Point(10, 110));
        var tris = SdkTriangulationTesterPanel.ComputeDelaunay(triPanel.Vertices, out var superTri);
        Require(tris.Count == 2 && superTri.Length == 3, "SdkTriangulationTesterPanel Delaunay triangle count mismatch.");
        string triTemp = Path.Combine(Path.GetTempPath(), $"xray_tri_{Guid.NewGuid():N}.txt");
        triPanel.ChooseVertexFileToOpen = () => triTemp;
        triPanel.ChooseVertexFileToSave = () => triTemp;
        try
        {
            var savedVertices = triPanel.Vertices.ToArray();
            triPanel.GetControl<Button>("m_save_button").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            triPanel.ClearVertices();
            triPanel.GetControl<Button>("m_open_button").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(triPanel.Vertices.SequenceEqual(savedVertices), "Triangulation vertex file round-trip changed the coordinates.");
            File.WriteAllLines(triTemp, new[] { "10\t10", "110,10", "110;110", "10 110", "NaN 0", "0 Infinity" });
            triPanel.LoadVerticesFromFile(triTemp);
            Require(triPanel.Vertices.SequenceEqual(savedVertices), "Triangulation did not load legacy delimiters or skip nonfinite coordinates.");
        }
        finally
        {
            if (File.Exists(triTemp))
                File.Delete(triTemp);
        }

        var pointerTriangulation = new SdkTriangulationTesterPanel();
        var triangulationWindow = new Window { Width = 560, Height = 360, Content = pointerTriangulation };
        triangulationWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var vertex = new Point(40, 40);
        var vertexInWindow = pointerTriangulation.CanvasControl.TranslatePoint(vertex, triangulationWindow)!.Value;
        triangulationWindow.MouseDown(vertexInWindow, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        triangulationWindow.MouseUp(vertexInWindow, MouseButton.Left);
        Require(pointerTriangulation.Vertices.SequenceEqual(new[] { vertex }), "Left-click did not add a vertex to the triangulation canvas.");
        triangulationWindow.MouseDown(vertexInWindow, MouseButton.Right, RawInputModifiers.RightMouseButton);
        triangulationWindow.MouseUp(vertexInWindow, MouseButton.Right);
        Require(pointerTriangulation.Vertices.Count == 0, "Right-click did not remove the nearby triangulation vertex.");
        triangulationWindow.Close();

        var wave5bShell = new SdkAvaloniaShellWindow(backgroundMode: false);
        wave5bShell.SetPanelContent(SdkSceneViewPanel.PersistId, scenePanel);
        wave5bShell.SetPanelContent(SdkProjectPanel.PersistId, projectPanel);
        wave5bShell.SetPanelContent(SdkObjectInspectorPanel.PersistId, inspectorPanel);
        wave5bShell.SetPanelContent(SdkToolsPanel.PersistId, toolsPanel);
        wave5bShell.SetPanelContent(SdkSceneGraphPanel.PersistId, sceneGraphPanel);
        wave5bShell.SetPanelContent(SdkCameraViewPanel.PersistId, cameraPanel);
        wave5bShell.SetPanelContent(SdkAnimationCurveEditorPanel.PersistId, animCurvesPanel);
        wave5bShell.SetPanelContent(SdkTriangulationTesterPanel.PersistId, triPanel);
        wave5bShell.DockFactory.ShowPanel(wave5bShell.DockLayout, SdkSceneGraphPanel.PersistId, "Scene graph");
        wave5bShell.DockFactory.ShowPanel(wave5bShell.DockLayout, SdkTriangulationTesterPanel.PersistId, "triangulation_tester");
        wave5bShell.Show();
        Dispatcher.UIThread.RunJobs();
        foreach (var (name, ctrl) in new (string Name, Control Ctrl)[]
        {
            ("scenePanel", scenePanel),
            ("inspectorPanel", inspectorPanel),
            ("toolsPanel", toolsPanel),
            ("triPanel", triPanel)
        })
        {
            Require(ReferenceEquals(TopLevel.GetTopLevel(ctrl), wave5bShell) && ctrl.Bounds.Width > 0 && ctrl.Bounds.Height > 0,
                $"wave5bShell did not mount {name} into visual tree with non-zero bounds.");
        }
        wave5bShell.ShowPanel(SdkProjectPanel.PersistId, "Project");
        Dispatcher.UIThread.RunJobs();
        Require(ReferenceEquals(TopLevel.GetTopLevel(projectPanel), wave5bShell) && projectPanel.Bounds.Width > 0 && projectPanel.Bounds.Height > 0,
            "wave5bShell did not mount projectPanel when switched back to active tab.");
        wave5bShell.Close();
        Dispatcher.UIThread.RunJobs();
        sceneVpHost.Dispose();
        sceneVpHost.ReleaseSurfaceLease();
        camHost.Dispose();
        camHost.ReleaseSurfaceLease();

        // Step 33: UI-5 Wave 5C all 8 IDE dialogs + 4 options pages
        var aboutWin = new SdkAboutWindow(2026, "2011-2026");
        aboutWin.Show();
        Dispatcher.UIThread.RunJobs();
        Require(aboutWin.BuildSummary.Contains("2026", StringComparison.Ordinal), "SdkAboutWindow summary mismatch.");
        aboutWin.Close();

        var logView = new SdkIdeLogView();
        int clears = 0;
        logView.Cleared += () => clears++;
        logView.AddMessage("Info msg");
        logView.AddWarning("Warn msg");
        logView.AddError("Err msg");
        Require(logView.Messages.SequenceEqual(new[] { "Info msg", "[WARN] Warn msg", "[ERROR] Err msg" }),
            "IDE log did not display messages in order with severity prefixes.");
        logView.GetControl<Button>("m_clear_button").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(logView.Messages.Count == 0 && clears == 1, "IDE log Clear did not empty the log and notify the host.");

        var resLoadWin = new SdkResourceLoadingWindow();
        resLoadWin.InitializeDialog("model", "missing_box");
        resLoadWin.RadioAllWithName.IsChecked = true;
        resLoadWin.SubstituteTextBox.Text = "props/woodbox_mil";
        Require(resLoadWin.AcceptReplace() && resLoadWin.DialogActionResult == subst_resource_result.subst_all_by_name &&
                resLoadWin.SelectedResourceName == "props/woodbox_mil",
            "SdkResourceLoadingWindow AcceptReplace failed.");
        resLoadWin.AcceptIgnore();
        Require(resLoadWin.DialogActionResult == subst_resource_result.ignore_all_by_name && !resLoadWin.DialogAccepted,
            "SdkResourceLoadingWindow AcceptIgnore failed.");

        var libWin = new SdkSelectLibraryClassWindow();
        libWin.Initialize(
            new[]
            {
                new SdkLibraryClassEntry("props/crates/woodbox", "base:solid_visual"),
                new SdkLibraryClassEntry("lights/sun", "base:light")
            },
            initial: null,
            filter: "light",
            readOnly: false);
        Require(libWin.SelectLibraryPathProgrammatically("lights/sun") && libWin.SelectedPath == "lights/sun" && libWin.OkButton.IsEnabled,
            "SdkSelectLibraryClassWindow filtered selection failed.");
        libWin.Initialize(
            new[] { new SdkLibraryClassEntry("lights/sun", "base:light") },
            initial: "lights/sun",
            filter: null,
            readOnly: true);
        Require(!libWin.OkButton.IsEnabled && libWin.IsReadOnlyMode, "SdkSelectLibraryClassWindow readOnly check failed.");

        string projBrowserRoot = Path.Combine(Path.GetTempPath(), $"xray_pb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(projBrowserRoot);
        try
        {
            string groupDir = Path.Combine(projBrowserRoot, "levels");
            string validProjDir = Path.Combine(groupDir, "demo_level");
            Directory.CreateDirectory(validProjDir);
            File.WriteAllText(Path.Combine(validProjDir, "project.xprj"), "project = {}");

            var pbWin = new SdkProjectBrowserWindow();
            pbWin.InitializeDialog(saveMode: false, projBrowserRoot, string.Empty);
            var levelsItem = pbWin.Items.FirstOrDefault(i => i.Name == "levels" && i.ItemType == "folder");
            Require(levelsItem != null, "SdkProjectBrowserWindow root folder missing.");
            pbWin.Close();

            var pbWin2 = new SdkProjectBrowserWindow();
            pbWin2.InitializeDialog(saveMode: false, projBrowserRoot, "levels/demo_level");
            Require(pbWin2.SelectedProject == "demo_level" || pbWin2.SelectedProject == "levels/demo_level", "SdkProjectBrowserWindow initial project check.");
            Require(pbWin2.Items.Any(i => i.Name == ".."), "SdkProjectBrowserWindow missing '..' entry inside subfolder.");
            pbWin2.BrowseBack();
            Require(!pbWin2.Items.Any(i => i.Name == ".."), "SdkProjectBrowserWindow BrowseBack did not return to root.");
            Require(!pbWin2.TryAcceptProjectNameProgrammatically("../escape", out _), "SdkProjectBrowserWindow must reject path traversal.");

            pbWin2.InitializeDialog(saveMode: true, projBrowserRoot, string.Empty);
            string createdFolder = pbWin2.CreateNewFolder();
            Require(Directory.Exists(Path.Combine(projBrowserRoot, createdFolder)), "SdkProjectBrowserWindow CreateNewFolder failed.");
            Require(pbWin2.TryAcceptProjectNameProgrammatically("new_save_proj", out string err) &&
                    Directory.Exists(Path.Combine(projBrowserRoot, "new_save_proj")) &&
                    string.IsNullOrEmpty(err),
                "SdkProjectBrowserWindow save mode auto-create failed.");
            pbWin2.Close();
        }
        finally
        {
            if (Directory.Exists(projBrowserRoot))
                Directory.Delete(projBrowserRoot, recursive: true);
        }

        var connectionView = new SdkPropertyConnectionView();
        var connectionWindow = new Window { Width = 560, Height = 520, Content = connectionView };
        string? connectedPair = null;
        connectionView.ConnectRequested += (source, target) => connectedPair = source + "->" + target;
        connectionView.SetEndpoints("Trigger", new[] { ("OnEnter", "Boolean") }, "Door", new[] { ("Open", "Boolean") });
        var connectButton = connectionView.GetControl<Button>("m_connect_btn");
        Require(!connectButton.IsEnabled && connectionView.LeftObjectLabel == "Trigger" && connectionView.RightObjectLabel == "Door",
            "Property connection view did not populate both endpoints and wait for a selection.");
        connectionView.SelectProperties("OnEnter", "Open");
        connectionView.SetViewMode(false);
        Require(!connectionView.IsDetailsMode && !connectionView.LeftProperties[0].ShowDetails && connectButton.IsEnabled,
            "Property connection view lost the selected pair when changing to list mode.");
        connectionWindow.Show();
        Dispatcher.UIThread.RunJobs();
        connectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(connectedPair == "OnEnter->Open", "Property connection view did not send the selected pair.");
        connectionWindow.Close();

        var resBrowserWin = new SdkResourceBrowserWindow
        {
            InspectDependenciesHandler = () => new SdkDependencyReport { ProjectName = "Test" },
            LoadResourceHandler = path => (Guid.NewGuid(), new SdkCookedCacheEntry
            {
                VirtualPath = path,
                CookedPhysicalPath = "cooked.bin",
                CacheHit = true,
                NativeBytes = 512,
                NativeUnits = 1
            })
        };
        resBrowserWin.PreviewRequested += (_, _) => { };
        resBrowserWin.ShowDependencies();
        Require(resBrowserWin.DetailsText.Contains("Dependencies: 0", StringComparison.Ordinal), "SdkResourceBrowserWindow ShowDependencies failed.");
        resBrowserWin.LoadPath("resources/models/box.model");
        Require(resBrowserWin.LastResult?.NativeBytes == 512 && resBrowserWin.DetailsText.Contains("Cache hit", StringComparison.Ordinal),
            "SdkResourceBrowserWindow LoadPath failed.");
        resBrowserWin.RequestPreview("resources/data/bad.bin");
        Require(resBrowserWin.DetailsText.Contains("Preview failed:", StringComparison.Ordinal), "SdkResourceBrowserWindow unsupported preview check failed.");
        resBrowserWin.Close();

        var optionsTarget = new SdkSceneViewPanel();
        bool trackActiveState = true;
        var mockInput = new MockInputOptionsAdapter();
        var optWin = new SdkOptionsWindow();
        optWin.RegisterPage("General", new SdkOptionsPageGeneral(optionsTarget));
        optWin.RegisterPage(@"General\View Panel", new SdkOptionsPageViewPanel(optionsTarget));
        optWin.RegisterPage(@"General\Project", new SdkOptionsPageProject(() => trackActiveState, v => trackActiveState = v));
        var inputPage = new SdkOptionsPageInput(mockInput);
        optWin.RegisterPage("Input", inputPage);
        optWin.Show();
        Dispatcher.UIThread.RunJobs();

        optWin.SelectPageByName("General");
        float startNear = optionsTarget.NearPlane;
        ((SdkOptionsPageGeneral)optWin.ActivePage!).NearPlaneBox.Value = 1.25m;
        optWin.SelectPageByName("Project");
        ((SdkOptionsPageProject)optWin.ActivePage!).TrackActiveItemCheck.IsChecked = false;
        optWin.SelectPageByName("Input");
        Require(inputPage.AssignShortcut("Save", "Ctrl+S(Global)") && optWin.ApplyButton.IsEnabled,
            "SdkOptionsWindow ApplyButton failed to enable across pages.");

        optWin.CancelAllChanges();
        Require(MathF.Abs(optionsTarget.NearPlane - startNear) < 1e-4f && trackActiveState &&
                mockInput.GetActionShortcutsWithContexts("Save").Count == 1,
            "SdkOptionsWindow CancelAllChanges rollback failed.");

        optWin.SelectPageByName("General");
        ((SdkOptionsPageGeneral)optWin.ActivePage!).NearPlaneBox.Value = 0.5m;
        optWin.AcceptAllChanges();
        Require(optWin.DialogAccepted && MathF.Abs(optionsTarget.NearPlane - 0.5f) < 1e-4f,
            "SdkOptionsWindow AcceptAllChanges commit failed.");
        optWin.Close();
        Dispatcher.UIThread.RunJobs();

        // Step 34: UI-6 Wave 6A specialized editors (Model, Material, Texture, RenderOptions, DCC + PoseView)
        var poseView = new SdkDccPoseView
        {
            Width = 320,
            Height = 240,
            Points = new[]
            {
                (new Vector3(0f, 0f, 0f), -1),
                (new Vector3(0f, 1f, 0f), 0),
                (new Vector3(0.5f, 1.5f, 0f), 1)
            }
        };
        var poseWin = new Window { Width = 340, Height = 260, Content = poseView };
        poseWin.Show();
        Dispatcher.UIThread.RunJobs();
        Require(poseView.Bounds.Width > 0 && poseView.Bounds.Height > 0, "SdkDccPoseView failed layout.");
        poseWin.Close();

        string wave6aRoot = Path.Combine(Path.GetTempPath(), $"xray_wave6a_{Guid.NewGuid():N}");
        string packRoot = Path.Combine(wave6aRoot, "pack");
        string workRoot = Path.Combine(wave6aRoot, "work");
        Directory.CreateDirectory(packRoot);
        Directory.CreateDirectory(workRoot);
        try
        {
            SdkVfsManager.ConfigureDefaultMounts(packRoot, workRoot);
            string sourcesDir = SdkVfsManager.WorkingSourcesRoot;

            string renderCfgDir = Path.Combine(sourcesDir, "render");
            Directory.CreateDirectory(renderCfgDir);
            File.WriteAllText(Path.Combine(renderCfgDir, "high.cfg"),
                "options = {\n  r_enabled_post_process_stage = true,\n  r_enabled_sun_stage = true,\n  r_unknown_flag = 1\n}\n");

            string appliedCfgMsg = string.Empty;
            var renderDoc = new SdkRenderOptionsDocument("resources/render/high.cfg", dict =>
            {
                appliedCfgMsg = string.Join(",", dict.Select(kv => kv.Key + "=" + kv.Value));
            });
            var renderWin = new SdkRenderOptionsEditorWindow(renderDoc);
            renderWin.Show();
            Dispatcher.UIThread.RunJobs();
            Require(renderWin.OptionRows.Count == 3 && renderWin.OptionRows.Any(r => r.Key == "r_enabled_post_process_stage" && r.IsEditable),
                "SdkRenderOptionsEditorWindow option rows mismatch.");
            var ppRow = renderWin.OptionRows.First(r => r.Key == "r_enabled_post_process_stage");
            ppRow.MappedValue = false;
            renderDoc.Apply();
            Require(!renderDoc.IsSaved && appliedCfgMsg.Contains("False", StringComparison.Ordinal),
                "SdkRenderOptionsEditorWindow toggle/apply failed.");
            renderWin.ClosePromptOverride = () => SdkDocumentSaveChoice.Cancel;
            Require(!renderWin.ConfirmClose(), "SdkRenderOptionsEditorWindow Cancel prompt failed.");
            renderWin.ClosePromptOverride = () => SdkDocumentSaveChoice.No;
            renderWin.Close();

            string texDir = Path.Combine(sourcesDir, "textures");
            Directory.CreateDirectory(texDir);
            byte[] tgaHeader = new byte[18];
            BitConverter.TryWriteBytes(tgaHeader.AsSpan(12, 2), (ushort)64);
            BitConverter.TryWriteBytes(tgaHeader.AsSpan(14, 2), (ushort)64);
            File.WriteAllBytes(Path.Combine(texDir, "box_diffuse.tga"), tgaHeader);

            string? previewedTex = null;
            var texDoc = new SdkTextureDocument("resources/textures/box_diffuse.tga", new MockTextureAuthoring());
            var texWin = new SdkTextureEditorWindow(texDoc, uri => previewedTex = uri);
            texWin.Show();
            Dispatcher.UIThread.RunJobs();
            texWin.ApplyDraftPreview();
            Require(!string.IsNullOrEmpty(previewedTex) && texWin.Grid.data_view.Count > 0,
                "SdkTextureEditorWindow preview or PropertyGrid check failed.");
            texWin.Close();

            string matDir = Path.Combine(sourcesDir, "materials");
            Directory.CreateDirectory(matDir);
            File.WriteAllText(Path.Combine(matDir, "wood.material"),
                "material = {\n  common = { stages = { g_stage = 1 } },\n  g_stage = {\n    effect = {\n      effect_id = \"default\",\n      constant_diffuse = { is_model_parameter = true, value = float4( 1.0, 1.0, 1.0, 1.0 ) },\n      texture_diffuse = { is_model_parameter = true, value = \"textures/box_diffuse\" },\n      constant_specular_power = { is_model_parameter = true, value = 16.0 },\n      fresnel_at_0_degree = { is_model_parameter = true, value = 0.25 }\n    }\n  }\n}\n");

            string? previewedMat = null;
            var matDoc = new SdkMaterialDocument("resources/materials/wood.material", new MockMaterialAuthoring());
            var matWin = new SdkMaterialEditorWindow(matDoc, uri => previewedMat = uri);
            matWin.Show();
            Dispatcher.UIThread.RunJobs();
            matWin.ApplyDraftPreview();
            Require(previewedMat == "preview://wood" && matWin.Grid.data_view.Count > 0,
                "SdkMaterialEditorWindow preview or PropertyGrid check failed.");
            matWin.Close();

            string modelDir = Path.Combine(sourcesDir, "models", "crate.model");
            Directory.CreateDirectory(modelDir);
            File.WriteAllText(Path.Combine(modelDir, "settings"),
                "material_settings = {\n  mesh0 = { material_name = \"resources/materials/wood.material\" }\n}\n");

            ulong previewedModel = 0;
            var modelDoc = new SdkModelDocument("resources/models/crate.model", new MockModelAuthoring());
            var modelWin = new SdkModelEditorWindow(modelDoc, h => previewedModel = h);
            modelWin.Show();
            Dispatcher.UIThread.RunJobs();
            modelDoc.AddCollision(ModelCollisionType.Box);
            modelWin.ApplyDraftPreview();
            Require(previewedModel == 77 && modelWin.Surfaces.Items.Count == 1 && modelWin.Bones.Items.Count == 1 &&
                    modelWin.CollisionList.Items.Count == 1,
                "SdkModelEditorWindow collision/surface/bone/preview check failed.");
            string objOut = Path.Combine(wave6aRoot, "crate.obj");
            modelWin.ExportObj(objOut);
            Require(File.Exists(objOut), "SdkModelEditorWindow OBJ export failed.");
            modelWin.ClosePromptOverride = () => SdkDocumentSaveChoice.Cancel;
            Require(!modelWin.ConfirmClose(), "SdkModelEditorWindow Cancel close prompt failed.");
            modelWin.ClosePromptOverride = () => SdkDocumentSaveChoice.No;
            modelWin.Close();

            Directory.CreateDirectory(Path.Combine(sourcesDir, "dcc"));
            var dccService = new SdkDccService(
                new SdkDccSettings { WatchSources = false },
                sourcesDir,
                Path.Combine(wave6aRoot, "dcc_cache"),
                new MockDccCooker(),
                new MockDccPublisher(),
                () => false,
                () => Guid.NewGuid());
            using (dccService)
            {
                var dccWin = new SdkDccEditorWindow(dccService, new MockDccResources(), Path.Combine(wave6aRoot, "dcc.json"));
                dccWin.Show();
                Dispatcher.UIThread.RunJobs();
                dccWin.SelectAsset("resources/models/crate.model");
                Require(dccWin.StatusText.Contains("Associate an editable scene", StringComparison.Ordinal),
                    "SdkDccEditorWindow SelectAsset status check failed.");
                dccWin.Close();
            }
        }
        finally
        {
            if (Directory.Exists(wave6aRoot))
                Directory.Delete(wave6aRoot, recursive: true);
        }

        // Step 35: UI-6 Wave 6B Animation Editors (Collection, Viewer, Setup, Groups)
        string wave6bRoot = Path.Combine(Path.GetTempPath(), $"xray_wave6b_{Guid.NewGuid():N}");
        string wave6bPack = Path.Combine(wave6bRoot, "pack");
        string wave6bWork = Path.Combine(wave6bRoot, "work");
        Directory.CreateDirectory(wave6bPack);
        Directory.CreateDirectory(wave6bWork);
        try
        {
            SdkVfsManager.ConfigureDefaultMounts(wave6bPack, wave6bWork);
            string sourcesDir = SdkVfsManager.WorkingSourcesRoot;

            using var animDcc = new MockDccResources();

            // 1. SdkAnimationCollectionEditorWindow
            var colDoc = new SdkAnimationCollectionDocument(
                "resources/animations/collections/test.anim_collection",
                new MockAnimationCollectionServices(),
                create: true);
            colDoc.AddClip(Array.Empty<int>(), "resources/animations/single/walk.clip");
            var colPreview = new SdkAnimationPreview(animDcc, h => new MockAnimationPreviewSurface(h));
            var colWin = new SdkAnimationCollectionEditorWindow(colDoc, colPreview);
            colWin.Show();
            Dispatcher.UIThread.RunJobs();
            Require(colWin.ItemsTree.ItemCount == 1, "SdkAnimationCollectionEditorWindow tree items mismatch.");
            colWin.ClosePromptOverride = () => SdkDocumentSaveChoice.Cancel;
            Require(!colWin.ConfirmClose(), "SdkAnimationCollectionEditorWindow Cancel prompt failed.");
            colWin.ClosePromptOverride = () => SdkDocumentSaveChoice.No;
            colWin.Close();
            Dispatcher.UIThread.RunJobs();

            // 2. SdkAnimationViewerEditorWindow
            var viewerDoc = new SdkAnimationViewerDocument(
                "resources/animations/viewer_scenes/test.avs",
                animDcc,
                create: true);
            int targetId = viewerDoc.AddNode(0);
            int clipId = viewerDoc.AddNode(1);
            viewerDoc.Connect(clipId, targetId, 1.0, 1.0);
            var viewerPreview = new SdkAnimationPreview(animDcc, h => new MockAnimationPreviewSurface(h));
            var viewerWin = new SdkAnimationViewerEditorWindow(viewerDoc, viewerPreview);
            viewerWin.Show();
            Dispatcher.UIThread.RunJobs();
            Require(viewerWin.Hypergraph.nodes.Count == 2 && viewerWin.Hypergraph.links.Count == 1,
                "SdkAnimationViewerEditorWindow hypergraph nodes/links mismatch.");
            viewerWin.ClosePromptOverride = () => SdkDocumentSaveChoice.Cancel;
            Require(!viewerWin.ConfirmClose(), "SdkAnimationViewerEditorWindow Cancel prompt failed.");
            viewerWin.ClosePromptOverride = () => SdkDocumentSaveChoice.No;
            viewerWin.Close();
            Dispatcher.UIThread.RunJobs();

            // 3. SdkAnimationSetupEditorPanel
            using var setupDoc = new SdkAnimationSetupDocument(
                "resources/animations/single/walk.clip",
                2.0f,
                clipDataConfig: null,
                optionsConfig: null);
            setupDoc.AddChannel("footsteps", SdkEventChannelType.Events);
            setupDoc.SetChannelKnotsAndDomains("footsteps", new[] { 15f }, new byte[] { 2 });
            setupDoc.AddChannel("anim_intervals", SdkEventChannelType.Partitions);
            setupDoc.SetChannelKnotsAndDomains("anim_intervals", new[] { 15f, 45f, 75f }, new byte[] { 1, 0 });
            var setupPanel = new SdkAnimationSetupEditorPanel(setupDoc);
            var setupWin = new Window { Width = 900, Height = 560, Content = setupPanel };
            setupWin.Show();
            Dispatcher.UIThread.RunJobs();
            Require(setupPanel.ChannelsList.ItemCount == 2 && setupPanel.SetupPanel.channels[0].objects.Single() is animation_channel_event marker &&
                    (float)marker.get_property("time")! == 500f && marker.type == 2,
                "Animation setup did not display the saved event at its frame time.");
            var partitions = setupPanel.SetupPanel.channels[1].objects.Cast<animation_channel_partition>().ToArray();
            Require(!setupPanel.SetupPanel.IsChannelEditingEnabled && partitions.Length == 3 &&
                    partitions[0].type == animation_channel_partition_type.left &&
                    partitions.Select(part => (float)part.get_property("length")!).SequenceEqual(new[] { 500f, 1000f, 500f }),
                "Animation setup did not display cyclic partitions at the saved boundaries.");
            setupWin.Content = null;
            Dispatcher.UIThread.RunJobs();
            setupWin.Content = setupPanel;
            Dispatcher.UIThread.RunJobs();
            Require(ReferenceEquals(setupPanel.Document, setupDoc), "Reattaching animation setup lost its document.");
            setupWin.Close();
            setupPanel.DetachDocument();
            Dispatcher.UIThread.RunJobs();

            // 4. SdkAnimationGroupsEditorPanel
            string ctrlDir = Path.Combine(sourcesDir, "animations", "controllers");
            string groupsDir = Path.Combine(ctrlDir, "groups");
            Directory.CreateDirectory(groupsDir);
            File.WriteAllText(
                Path.Combine(groupsDir, "idle.lua"),
                "animation_group = {\n  flags = \"idle\",\n  gait = \"stand\",\n  mix_time = 200,\n  value = 1.0,\n  animations = {\n    { animation = \"resources/animations/single/walk.clip\", id = 0, min_weight = 0.0, max_weight = 1.0 }\n  }\n}\n");
            File.WriteAllText(
                Path.Combine(ctrlDir, "test.lua"),
                "animation_controller_set = {\n  model = \"neutral_01\",\n  fixed_bones = {},\n  animation_groups = { \"resources/animations/controllers/groups/idle.lua\" }\n}\n");

            using var groupsDoc = SdkAnimationGroupsDocument.Open("resources/animations/controllers/test.lua");
            var groupsPanel = new SdkAnimationGroupsEditorPanel(groupsDoc);
            var groupsWin = new Window { Width = 960, Height = 600, Content = groupsPanel };
            groupsWin.Show();
            Dispatcher.UIThread.RunJobs();
            Require(groupsPanel.GroupsList.ItemCount == 1 && groupsPanel.Hypergraph.nodes.Count == 1 && groupsPanel.Grid.data_view.Count > 0,
                "SdkAnimationGroupsEditorPanel nodes/groups/grid mismatch.");
            groupsWin.Close();
            groupsPanel.DetachDocument();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            if (Directory.Exists(wave6bRoot))
                Directory.Delete(wave6bRoot, recursive: true);
        }

        // Step 36: UI-6 Wave 6C Audio & Particle Editors (SdkSoundEditorPanel + WaveformView, SdkParticleEditorPanel)
        string wave6cRoot = Path.Combine(Path.GetTempPath(), $"xray_wave6c_{Guid.NewGuid():N}");
        string wave6cPack = Path.Combine(wave6cRoot, "pack");
        string wave6cWork = Path.Combine(wave6cRoot, "work");
        Directory.CreateDirectory(wave6cPack);
        Directory.CreateDirectory(wave6cWork);
        try
        {
            SdkVfsManager.ConfigureDefaultMounts(wave6cPack, wave6cWork);
            string sourcesDir = SdkVfsManager.WorkingSourcesRoot;

            // 1. Sound resources + SdkSoundEditorPanel
            string singleDir = Path.Combine(sourcesDir, "sounds", "single");
            string colDir = Path.Combine(sourcesDir, "sounds", "collections");
            string compDir = Path.Combine(sourcesDir, "sounds", "composite");
            string sceneDir = Path.Combine(sourcesDir, "sounds", "scenes");
            Directory.CreateDirectory(singleDir);
            Directory.CreateDirectory(colDir);
            Directory.CreateDirectory(compDir);
            Directory.CreateDirectory(sceneDir);

            const int sampleRate = 44100;
            const int frames = 4410;
            int dataLen = frames * 2;
            byte[] wavBytes = new byte[44 + dataLen];
            System.Text.Encoding.ASCII.GetBytes("RIFF", 0, 4, wavBytes, 0);
            BitConverter.GetBytes(36 + dataLen).CopyTo(wavBytes, 4);
            System.Text.Encoding.ASCII.GetBytes("WAVE", 0, 4, wavBytes, 8);
            System.Text.Encoding.ASCII.GetBytes("fmt ", 0, 4, wavBytes, 12);
            BitConverter.GetBytes(16).CopyTo(wavBytes, 16);
            BitConverter.GetBytes((ushort)1).CopyTo(wavBytes, 20);
            BitConverter.GetBytes((ushort)1).CopyTo(wavBytes, 22);
            BitConverter.GetBytes(sampleRate).CopyTo(wavBytes, 24);
            BitConverter.GetBytes(sampleRate * 2).CopyTo(wavBytes, 28);
            BitConverter.GetBytes((ushort)2).CopyTo(wavBytes, 32);
            BitConverter.GetBytes((ushort)16).CopyTo(wavBytes, 34);
            System.Text.Encoding.ASCII.GetBytes("data", 0, 4, wavBytes, 36);
            BitConverter.GetBytes(dataLen).CopyTo(wavBytes, 40);
            for (int i = 0; i < frames; i++)
            {
                short s = (short)(MathF.Sin(2f * MathF.PI * 440f * i / sampleRate) * 16000f);
                BitConverter.GetBytes(s).CopyTo(wavBytes, 44 + i * 2);
            }
            File.WriteAllBytes(Path.Combine(singleDir, "test_beep.wav"), wavBytes);

            File.WriteAllText(
                Path.Combine(colDir, "test_col.sound_collection"),
                "collection = {\n  type = \"random\",\n  cyclic_repeat_from_sound = 0,\n  dont_repeat_sound_successively = false,\n  min_delay = 0.0,\n  max_delay = 0.0,\n  sound_items = {\n    { filename = \"test_beep\", resource_type = 256 }\n  }\n}\n");
            File.WriteAllText(
                Path.Combine(compDir, "test_comp.composite_sound"),
                "composite_sound = {\n  before_playing_pause = 0,\n  after_playing_pause = 0,\n  sound_items = {\n    { filename = \"test_beep\", resource_type = 256, offset_type = \"constant\", playing_offset = 0 }\n  }\n}\n");
            File.WriteAllText(
                Path.Combine(sceneDir, "test_scene.editor_sound_scene"),
                "scene = {\n  { id = \"11111111-1111-1111-1111-111111111111\", filename = \"test_beep\", resource_type = 256, looped = false, position = float3( 2.0, 0.0, 0.0 ), rotation = float3( 0.0, 0.0, 0.0 ) }\n}\n");

            var soundPanel = new SdkSoundEditorPanel();
            var soundWin = new Window { Width = 960, Height = 640, Content = soundPanel };
            soundWin.Show();
            Dispatcher.UIThread.RunJobs();

            var singleSound = soundPanel.LoadSingle("test_beep");
            Dispatcher.UIThread.RunJobs();
            float splMid = singleSound.EvaluateSplDb(15f);
            Require(singleSound.WavData != null &&
                    soundPanel.WaveformView.Bounds.Width > 0 &&
                    soundPanel.SplCurvePanel.panel_curves.Count == 1 &&
                    soundPanel.Grid.data_view.Count > 0 &&
                    float.IsFinite(splMid) && splMid >= 20f && splMid <= 60f,
                "SdkSoundEditorPanel single sound/waveform/curve/grid check failed.");

            singleSound.Playback.Play(loop: false, triggerHardware: false);
            singleSound.Playback.Tick(0.05);
            Dispatcher.UIThread.RunJobs();
            Require(singleSound.Playback.IsPlaying && singleSound.Playback.CurrentRms > 0f,
                "SdkSoundEditorPanel playback/RMS check failed.");
            soundPanel.StopAllPlayback();

            soundPanel.LoadCollection("test_col");
            soundPanel.LoadComposite("test_comp");
            soundPanel.LoadScene("test_scene");
            Dispatcher.UIThread.RunJobs();
            Require(soundPanel.CollectionDocument?.Data.Items.Length == 1 &&
                    soundPanel.CompositeDocument?.Data.Items.Length == 1 &&
                    soundPanel.SceneDocument?.Instances.Length == 1 &&
                    soundPanel.Grid.data_view.Count > 0 &&
                    soundPanel.ConfirmClose(),
                "SdkSoundEditorPanel collection/composite/scene check failed.");
            soundWin.Content = null;
            Dispatcher.UIThread.RunJobs();
            soundWin.Content = soundPanel;
            Dispatcher.UIThread.RunJobs();
            Require(ReferenceEquals(soundPanel.SingleDocument, singleSound) && soundPanel.SceneDocument != null,
                "Reattaching the sound panel lost its open documents.");
            soundWin.Close();
            soundPanel.DisposeDocuments();
            Dispatcher.UIThread.RunJobs();

            // 2. Particle resources + SdkParticleEditorPanel
            string particleLibDir = Path.Combine(sourcesDir, "library", "particle_configs");
            Directory.CreateDirectory(particleLibDir);
            string particleLibPath = Path.Combine(particleLibDir, "particle_source.library");
            File.WriteAllText(
                particleLibPath,
                "particle_nodes = {\n" +
                "  Root = { type = \"emitter\", image_type = 3, particle_entity_type = \"particle_entity_root_type\" },\n" +
                "  PEmitter = { type = \"emitter\", image_type = 3, particle_entity_type = \"particle_entity_emitter_type\" },\n" +
                "  PGroup = { type = \"emitter\", image_type = 3, particle_entity_type = \"particle_entity_particle_group_type\" },\n" +
                "  Force = { type = \"action\", image_type = 4, particle_entity_type = \"particle_action_acceleration_type\" }\n" +
                "}\n" +
                "particle_node_properties = {\n" +
                "  Root = { default_values = { Name = \"Root\", Enabled = true, TotalLength = 10.0 } },\n" +
                "  PEmitter = { default_values = { Name = \"PEmitter\", Enabled = true, Delay = 0.0, Duration = 3.0, WorldSpace = true, MaxParticles = 100 } },\n" +
                "  PGroup = { default_values = { Name = \"PGroup\", Enabled = true } },\n" +
                "  Force = { default_values = { Name = \"Force\", Enabled = true } }\n" +
                "}\n" +
                "particle_toolbar = {\n" +
                "  { name = \"Emitters\", children = { \"PEmitter\", \"PGroup\" } },\n" +
                "  { name = \"Actions\", children = { \"Force\" } }\n" +
                "}\n" +
                "domain_types = {\n" +
                "  Point = {}\n" +
                "}\n");

            var particleLib = SdkParticleLibrary.LoadFromFile(particleLibPath);
            var particleDoc = SdkParticleDocument.CreateNew("resources/particles/test_fx.particle", particleLib);
            var emitterNode = particleDoc.AddNode(particleDoc.RootId, "PEmitter");
            particleDoc.AddNode(emitterNode.Id, "Force");

            var particlePanel = new SdkParticleEditorPanel();
            particlePanel.LoadDocument(particleDoc);
            var particleWin = new Window { Width = 1120, Height = 680, Content = particlePanel };
            particleWin.Show();
            Dispatcher.UIThread.RunJobs();

            var simSnapshot = particlePanel.StepPreview(0.1f);
            Require(particlePanel.Hypergraph.nodes.Count >= 3 &&
                    particlePanel.Hypergraph.links.Count >= 2 &&
                    particlePanel.TimelineItems.Count >= 2 &&
                    particlePanel.Grid.data_view.Count > 0 &&
                    simSnapshot != null,
                "SdkParticleEditorPanel hypergraph/timeline/grid/simulation check failed.");

            particlePanel.ClosePromptOverride = () => SdkDocumentSaveChoice.Cancel;
            Require(!particlePanel.ConfirmClose(), "SdkParticleEditorPanel Cancel prompt failed.");
            particlePanel.ClosePromptOverride = () => SdkDocumentSaveChoice.No;
            Require(particlePanel.ConfirmClose(), "SdkParticleEditorPanel No prompt failed.");

            var asyncToken = particlePanel.BeginPendingAsyncLoad(out var completeCancelledLoad);
            particlePanel.CancelPendingPreviewAndLoads();
            Require(asyncToken.IsCancellationRequested && particlePanel.LastCloseCancelledPendingWork,
                "SdkParticleEditorPanel async load cancellation check failed.");
            _ = particlePanel.BeginPendingAsyncLoad(out var completeNewLoad);
            completeCancelledLoad();
            Require(particlePanel.PendingAsyncLoadCount == 1, "An older particle load completed a newer generation's work.");
            completeNewLoad();

            particleWin.Content = null;
            Dispatcher.UIThread.RunJobs();
            particleWin.Content = particlePanel;
            Dispatcher.UIThread.RunJobs();
            Require(ReferenceEquals(particleDoc, particlePanel.Document) && particlePanel.Hypergraph.nodes.Count >= 3,
                "Reattaching the particle panel lost the graph or document.");
            particleWin.Close();
            particlePanel.DisposePanelResources();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            if (Directory.Exists(wave6cRoot))
                Directory.Delete(wave6cRoot, recursive: true);
        }

        VerifyDialogLogicAndNavigationEditors();

        Console.WriteLine("PASS: xray_avalonia_headless_smoke");
        return 0;
    }

    private static void VerifyToolLibraryActivation()
    {
        var panel = new SdkToolsPanel();
        var library = panel.ToolTabs["lights"];
        var first = new SdkToolLibraryEntry("light", "fixtures/fill", (int)images16x16.object_light);
        var second = new SdkToolLibraryEntry("light", "fixtures/key", (int)images16x16.object_light);
        library.PopulateLibrary(new[] { first, second });
        panel.ActivateTab(1);

        var window = new Window { Width = 280, Height = 460, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var headerPositions = panel.TabControl.Items.OfType<TabItem>()
                .Select(tab => tab.TranslatePoint(default, panel)).ToArray();
            Require(headerPositions.Length == 9 && headerPositions.All(point => point.HasValue && point.Value.X < panel.Bounds.Width) &&
                    headerPositions.Select(point => point?.Y).Distinct().Count() > 1 && library.Bounds.Height >= 260,
                "Tools panel did not wrap all nine tab headers into its docked width.");

            int selections = 0;
            int tabChanges = 0;
            var activations = new List<SdkToolLibraryEntry>();
            panel.LibrarySelectionChanged += () => selections++;
            panel.ActiveToolChanged += _ => tabChanges++;
            panel.LibraryEntryActivated += activations.Add;
            library.LibraryTree.TrackActiveNode(first.Path);
            library.LibraryTree.InnerTreeView.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Require(selections > 0 && tabChanges == 0 && activations.SequenceEqual(new[] { first }),
                "Tools panel did not route keyboard activation to the selected entry.");

            var keyLabel = library.GetVisualDescendants().OfType<TextBlock>().Single(label => label.Text == "key");
            var point = keyLabel.TranslatePoint(new Point(3, keyLabel.Bounds.Height / 2), window)!.Value;
            for (int click = 0; click < 2; click++)
            {
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(point, MouseButton.Left);
            }
            Require(activations.SequenceEqual(new[] { first, second }) && panel.SelectedEntry == second,
                $"Double-clicking a library row did not activate that row's resource. Selected: {panel.SelectedEntry?.Path}. " +
                $"Activated: {string.Join(", ", activations.Select(entry => entry.Path))}.");

            library.LibraryTree.FilterText = "key";
            library.PopulateLibrary(new[] { first, second });
            Require(panel.SelectedEntry == null && !library.LibraryTree.GetNode(first.Path)!.IsVisible &&
                    library.LibraryTree.GetNode(second.Path)!.IsVisible,
                "Refreshing the tool library retained a stale item or dropped the filter.");
        }
        finally { window.Close(); }
    }

    private static void VerifyProjectTreeInteraction()
    {
        var panel = new SdkProjectPanel();
        var root = new SdkProjectTreeEntry("World", "World", true, false, false, "", null, new[]
        {
            new SdkProjectTreeEntry("Lamp", "World/Lamp", false, false, false, "light", new object()),
            new SdkProjectTreeEntry("Box", "World/Box", false, false, false, "solid_visual", new object())
        });
        panel.Populate(root);
        var window = new Window { Width = 400, Height = 450, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var lamp = panel.TreeView.GetNode("World/Lamp")!;
            var editor = panel.FindControl<TextBox>("m_rename_box")!;
            var requests = new List<(SdkTreeNode Node, string Name)>();
            panel.RenameRequested += (node, name) => requests.Add((node, name));
            panel.BeginRename(lamp);
            Require(editor.IsEffectivelyVisible && editor.IsFocused && editor.Text == "Lamp",
                "Project tree did not focus the rename field for the requested node.");
            editor.Text = "Desk lamp";
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Require(requests.Count == 1 && requests[0] == (lamp, "Desk lamp") &&
                    panel.TreeView.SelectedNode == lamp,
                "Project tree did not commit the rename for the requested node.");

            panel.BeginRename(lamp);
            editor.Text = "Abandoned rename";
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Require(requests.Count == 1 && !editor.IsEffectivelyVisible, "Project tree committed a canceled rename.");

            panel.TrackSelection(new[] { "World/Lamp", "World/Box" });
            Dispatcher.UIThread.RunJobs();
            var label = panel.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == "Lamp");
            var start = label.TranslatePoint(new Point(4, label.Bounds.Height / 2), window)!.Value;
            var end = start + new Avalonia.Vector(12, 0);
            SdkTreeNode? dragSource = null;
            int dragSelectionCount = 0;
            panel.PrepareDrag = node =>
            {
                dragSource = node;
                dragSelectionCount = panel.TreeView.SelectedNodes.Count;
                return null;
            };
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Require(dragSource == lamp && dragSelectionCount == 2,
                "The project tree did not keep both items selected when starting a drag.");

            panel.TrackSelection(new[] { "World/Lamp", "World/Box" });
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(start, MouseButton.Left);
            Require(panel.TreeView.SelectedNodes.Count == 1 && panel.TreeView.SelectedNode == lamp,
                "A plain click did not select just the clicked row.");

            panel.TreeView.FilterText = "Lamp";
            panel.Populate(root);
            Require(panel.TreeView.GetNode("World/Lamp")!.IsVisible &&
                    !panel.TreeView.GetNode("World/Box")!.IsVisible,
                "Project tree lost the active filter after rebuilding.");
        }
        finally { window.Close(); }
    }

    private static void VerifyWorkspaceInspectorRange()
    {
        var view = new SdkObjectInspectorPanel();
        var model = new InspectorRangeModel();

        TextBox ShowValue(bool readOnly)
        {
            view.ShowProperties(model, readOnly);
            var row = view.PropertyGridControl.grid_items.Single();
            var editor = (single_editor)((named_item_editor)row.item_editor!).value_editor!;
            return editor.FindControl<TextBox>("text_box")!;
        }

        var box = ShowValue(readOnly: false);
        Require(box.Text == model.Value.ToString("F3", CultureInfo.CurrentCulture),
            "Inspector did not use the precision from the shared numeric range.");
        box.Text = "9";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Require(DataValidationErrors.GetHasErrors(box) && model.Value == 0.5f,
            "Inspector accepted a value outside the shared range.");
        box.Text = "1.25";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Require(model.Value == 1.25f, "Inspector did not commit a valid typed value.");
        box = ShowValue(readOnly: true);
        box.Text = "1.75";
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Require(box.IsReadOnly && model.Value == 1.25f, "Inspector wrote to a read-only property.");
    }

    private sealed class InspectorRangeModel
    {
        [SdkNumericRange(0, 2, 0.05, 3, UpdateOnEditComplete = true)]
        public float Value { get; set; } = 0.5f;
    }

    private static void VerifyDialogLogicAndNavigationEditors()
    {
        var temp = Directory.CreateTempSubdirectory("xray-wave6d-");
        try
        {
            string pack = Directory.CreateDirectory(Path.Combine(temp.FullName, "pack")).FullName;
            string work = Directory.CreateDirectory(Path.Combine(temp.FullName, "work")).FullName;
            SdkVfsManager.ConfigureDefaultMounts(pack, work);

            using var dialog = new SdkDialogEditorPanel();
            string path = Path.Combine(work, "wave6d.dlg");
            var localization = SdkDialogLocalizationStorage.CreateEmpty();
            localization.AddLanguage("english");
            localization.CurrentLanguage = "english";
            var document = dialog.CreateNew(path, localization);
            var root = document.AddNode(true, false, "Start");
            var reply = document.AddNode(false, false, "Reply", new Vector2(250, 40));
            document.AddLink(root.Id, reply.Id);
            dialog.SelectNode(root.Id);

            var dialogWindow = new Window { Width = 1100, Height = 720, Content = dialog };
            dialogWindow.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Require(dialog.Hypergraph.nodes.Count == 2 && dialog.Hypergraph.links.Count == 1 &&
                        dialog.SelectedNodeId == root.Id && dialog.PropertyGrid.selected_object != null,
                    "Dialog editor did not bind the graph, links and selected properties.");

                var editors = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Where(box => box.DataContext is SdkDialogLocalizationRow row && row.Id == root.StringTableId)
                    .ToArray();
                Require(editors.Length == 2 && editors[1].Focus(), "Dialog localization row did not render both editors.");
                editors[1].SetCurrentValue(TextBox.TextProperty, "Translated start");
                Require(!dialog.ConfirmClose(_ => SdkDocumentSaveChoice.Cancel) &&
                        localization.GetExactLanguageText(root.StringTableId, "english") == "Translated start",
                    "Dialog close did not commit the focused translation before prompting.");
                Require(document.Commands.rollback(-1) == -1 &&
                        localization.GetExactLanguageText(root.StringTableId, "english") == string.Empty,
                    "Dialog localization edit could not be undone.");
                Require(document.Commands.rollback(1) == 1 &&
                        dialog.CompileAndResetRuntime().Nodes.Count == 2,
                    "Dialog editor did not redo and compile the edited document.");
                Require(dialog.ConfirmClose(_ => SdkDocumentSaveChoice.Yes) && !document.IsDirty &&
                        File.Exists(path) && File.Exists(path + ".layout"),
                    "Dialog editor did not save the graph and its layout on close.");

                dialog.OpenFile(path, localization);
                Require(dialog.Hypergraph.links.Count == 1 && dialog.LastCompiledDialog == null &&
                        dialog.Document!.Nodes[reply.Id].LayoutPosition == new Vector2(250, 40),
                    "Dialog editor did not restore the saved graph and reset compilation state.");
            }
            finally { dialogWindow.Close(); }

            using var commands = new CommandEngine();
            var logicProject = new Wave6dLogicProject();
            var logic = new SdkLogicEditorPanel(logicProject, commands);
            int connectionRequests = 0;
            logic.OpenConnectionEditorRequested += () => connectionRequests++;
            logic.FindControl<Button>("NewJobButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            logic.FindControl<Button>("ConnectionEditorButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            logic.FindControl<Button>("StartButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            logic.FindControl<Button>("StepButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var trace = logic.FindControl<ListBox>("TraceList")!.Items;
            Require(logic.Hypergraph.nodes.Count == 2 && logic.Hypergraph.links.Count == 1 &&
                    logic.SelectedObjectId == logicProject.Job && logic.PropertyGrid.selected_object != null &&
                    trace.Count == 2 && Equals(trace[0], "#StartScene") && connectionRequests == 1,
                "Logic editor did not bind graph selection, runtime trace or the connection editor action.");

            var navigationProject = new Wave6dNavigationProject(Path.Combine(work, "navmesh.lua"));
            var navigation = new SdkNavigationEditorPanel(navigationProject, commands);
            navigation.FindControl<NumericUpDown>("CellSizeBox")!.Value = 2m;
            var generated = navigation.GenerateForProject();
            Require(generated.TriangleCount == 1 && navigationProject.CellSize == 2f && commands.CanUndo,
                "Navigation editor did not generate a mesh with the selected settings.");
            navigation.SaveProjectNavMesh();
            navigation.FindControl<TextBox>("StartBox")!.Text = "0.1, 0, 0.1";
            navigation.FindControl<TextBox>("GoalBox")!.Text = "0.5, 0, 0.5";
            navigation.FindControl<Button>("QueryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(navigation.LastPathResult is { Success: true } result && result.Waypoints.Count == 2,
                "Navigation editor did not query the mesh from the coordinate controls.");
            Require(commands.rollback(-1) == -1 && navigationProject.NavMesh == null,
                "Navigation editor did not restore the previous mesh on undo.");
            navigation.RefreshStats();
            Require(navigation.LastPathResult == null, "Navigation editor retained a path from the discarded mesh.");
            var loaded = navigation.LoadProjectNavMesh();
            Require(loaded.TriangleCount == 1 && loaded.SourceFilePath == navigationProject.GetNavMeshPath(false),
                "Navigation editor did not reload the saved mesh.");
        }
        finally { temp.Delete(recursive: true); }
    }

    private sealed class Wave6dLogicProject : ISdkLogicProject
    {
        private readonly Guid _scene = Guid.NewGuid();
        private readonly List<string> _trace = new();
        private bool _jobCreated;

        public Guid Job { get; } = Guid.NewGuid();
        public IReadOnlyList<string> EventTrace => _trace;

        public SdkLogicGraph ReadGraph()
        {
            var nodes = new List<SdkLogicGraphNode>
            {
                new(_scene, "[Scene] Test", true, false, null, new { Name = "Test" })
            };
            var links = new List<(Guid, Guid)>();
            if (_jobCreated)
            {
                nodes.Add(new(Job, "[Job:lights] Lamp", false, true, null, new { Name = "Lamp" }));
                links.Add((_scene, Job));
            }
            return new SdkLogicGraph(nodes, links, Array.Empty<string>());
        }

        public Guid? CreateObject(bool isScene)
        {
            _jobCreated = true;
            return Job;
        }

        public void Start() => _trace.Add("#StartScene");
        public void Tick(float seconds) => _trace.Add($"Step {seconds:F1}s");
    }

    private sealed class Wave6dNavigationProject(string path) : ISdkNavigationProject
    {
        public bool IsOpen => true;
        public SdkNavMesh? NavMesh { get; set; }
        public float CellSize { get; private set; }
        public string GetNavMeshPath(bool forWrite) => path;

        public SdkNavMesh GenerateNavMesh(SdkNavMeshGenerationSettings settings, IReadOnlyCollection<Guid>? excludedObjectGuids)
        {
            CellSize = settings.CellSize;
            var mesh = new SdkNavMesh();
            mesh.Vertices.AddRange(new[] { Vector3.Zero, new Vector3(0, 0, 2), new Vector3(2, 0, 0) });
            mesh.Indices.AddRange(new[] { 0, 1, 2 });
            mesh.TriangleData.Add(new SdkNavMeshTriangleData());
            return mesh;
        }
    }

    private sealed class MockAnimationCollectionServices : ISdkAnimationCollectionServices
    {
        public void ValidateClips(IReadOnlyList<string> paths, System.Threading.CancellationToken cancellation) { }
    }

    private sealed class MockAnimationPreviewSurface(ulong modelHandle) : ISdkAnimationPreviewSurface
    {
        public bool Ready => true;
        public ulong Window => modelHandle;
        public void Pose(Matrix4x4[] matrices) { }
        public void Dispose() { }
    }

    private sealed class MockTextureAuthoring : ISdkTextureAuthoring
    {
        public void ValidatePreview(string uri, string folder, System.Threading.CancellationToken cancellation) { }
        public void ReleasePreview(string prefix) { }
        public void Publish(string uri, string physical, System.Threading.CancellationToken cancellation) { }
    }

    private sealed class MockMaterialAuthoring : ISdkMaterialAuthoring
    {
        private sealed class Prepared : ISdkPreparedMaterial
        {
            public string PreviewUri => "preview://wood";
            public void Commit() { }
            public void FinishPreview() { }
            public void Dispose() { }
        }
        public ISdkPreparedMaterial Prepare(string uri, string draft, bool dependents, System.Threading.CancellationToken token) => new Prepared();
    }

    private sealed class MockModelAuthoring : ISdkModelPhysicsAuthoring
    {
        private sealed class Prepared : ISdkPreparedModel
        {
            public ulong Resource => 77;
            public void Commit() { }
            public void Dispose() { }
        }

        public SdkModelData Describe(string uri, System.Threading.CancellationToken token) => new(
            uri,
            new Vector3(-1, -1, -1),
            new Vector3(1, 1, 1),
            new[]
            {
                new SdkModelSurface(
                    "mesh0",
                    "resources/materials/wood.material",
                    new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
                    new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) },
                    new ushort[] { 0, 1, 2 })
            },
            new[] { new SdkModelBone("root", Matrix4x4.Identity) });

        public ISdkPreparedModel Prepare(string uri, LuaConfig draft, System.Threading.CancellationToken token) => new Prepared();
        public ISdkPreparedModel PreparePhysics(string uri, LuaConfig settings, LuaConfig collision, System.Threading.CancellationToken token) => new Prepared();
    }

    private sealed class MockDccCooker : ISdkDccCooker
    {
        public System.Text.Json.JsonElement Cook(SdkDccSettings settings, SdkDccLink link, string source, string output, System.Threading.CancellationToken token, Action check) => default;
    }

    private sealed class MockDccPublisher : ISdkDccPublisher
    {
        public ISdkDccPrepared Prepare(SdkDccLink link, string generation, System.Text.Json.JsonElement result, System.Threading.CancellationToken token)
            => throw new NotSupportedException();
    }

    private sealed class MockDccResources : ISdkDccResources
    {
        public void Mount(string virtualPrefix, string physicalDirectory, bool readOnly) { }
        public SdkNativeResource Query(string virtualPath, SdkResourceKind kind, System.Threading.CancellationToken token) => new(1, 1, 1);
        public void Release(ulong handle) { }
        public SdkModelData DescribeModel(ulong handle) => throw new NotSupportedException();
        public SdkNativeResource PrepareModel(ulong source, string identity, IReadOnlyDictionary<string, ulong> materials, System.Threading.CancellationToken token) => new(1, 1, 1);
        public void CheckDccCapabilities() { }
        public SdkAnimationSample SampleAnimation(ulong resource, float seconds) => new(1f, Array.Empty<SdkAnimationBone>());
        public void Dispose() { }
    }

    private sealed class MockInputOptionsAdapter : ISdkInputOptionsAdapter
    {
        private readonly Dictionary<string, List<string>> m_bindings = new(StringComparer.Ordinal)
        {
            ["Save"] = new List<string> { "Ctrl+Alt+S(Global)" },
            ["Undo"] = new List<string> { "Ctrl+Z(Global)" }
        };

        public List<string> GetActionNames() => m_bindings.Keys.ToList();

        public List<string> GetActionShortcutsWithContexts(string actionName) =>
            m_bindings.TryGetValue(actionName, out var list) ? new List<string>(list) : new List<string>();

        public bool AddShortcut(string actionName, string combination)
        {
            if (!m_bindings.TryGetValue(actionName, out var list))
                return false;
            list.Add(combination);
            return true;
        }

        public bool RemoveShortcut(string actionName, string combination) =>
            m_bindings.TryGetValue(actionName, out var list) && list.Remove(combination);

        public void ResetToDefaults()
        {
            m_bindings["Save"] = new List<string> { "Ctrl+Alt+S(Global)" };
            m_bindings["Undo"] = new List<string> { "Ctrl+Z(Global)" };
        }

        public Dictionary<string, List<string>> CaptureSnapshot() =>
            m_bindings.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value), StringComparer.Ordinal);

        public void RestoreSnapshot(Dictionary<string, List<string>> snapshot)
        {
            m_bindings.Clear();
            foreach (var kv in snapshot)
                m_bindings[kv.Key] = new List<string>(kv.Value);
        }
    }

    private sealed class RecordingSurfaceBackend : INativeSurfaceHostBackend
    {
        public string HandleDescriptor => "HWND";
        public nint ParkingHandle { get; private set; }
        public nint SurfaceHandle { get; private set; }
        public int LastWidth { get; private set; }
        public int LastHeight { get; private set; }
        public bool SurfaceDestroyed { get; private set; }
        public bool ParkingDestroyed { get; private set; }

        public nint CreateParkingWindow() => ParkingHandle = 0x1000;
        public nint CreateSurfaceWindow(nint parentHandle, int width, int height)
        {
            LastWidth = width;
            LastHeight = height;
            return SurfaceHandle = 0x2000;
        }
        public void ReparentSurface(nint surfaceHandle, nint newParentHandle) { }
        public void ResizeSurface(nint surfaceHandle, int width, int height)
        {
            LastWidth = width;
            LastHeight = height;
        }
        public void DestroySurface(nint surfaceHandle) => SurfaceDestroyed = true;
        public void DestroyParkingWindow(nint parkingHandle) => ParkingDestroyed = true;
    }

    private enum SampleMode
    {
        Static,
        Dynamic,
        Kinematic
    }

    private sealed class TestPropertyModel
    {
        public TestPropertyModel()
        {
            GradientMatrix = new color_matrix(new Size(1, 1));
            GradientMatrix.add_row(new List<color_matrix_element>
            {
                new() { color = new color_rgb(0, 0, 0, 1), position = new Point(0, 0) },
                new() { color = new color_rgb(1, 1, 1, 1), position = new Point(1, 0) }
            });
            GradientMatrix.add_row(new List<color_matrix_element>
            {
                new() { color = new color_rgb(0, 1, 0, 1), position = new Point(0, 1) },
                new() { color = new color_rgb(1, 1, 1, 1), position = new Point(1, 1) }
            });
        }

        [Category("General")]
        [DisplayName("Enabled")]
        [DefaultValue(true)]
        public bool BoolProp { get; set; } = true;

        [Category("General")]
        [DisplayName("Count")]
        [DefaultValue(7)]
        [refresh_grid_on_change_attribute]
        [value_range_and_format_attribute(0, 200, 1, 0)]
        public int IntProp { get; set; } = 7;

        [Category("General")]
        [DisplayName("Intensity")]
        [DefaultValue(0.5f)]
        [value_range_and_format_attribute(0.0, 2.0, 0.05, 2)]
        public float FloatProp { get; set; } = 0.5f;

        [Category("General")]
        [DisplayName("Scale")]
        public double DoubleProp { get; set; } = 1.0;

        [Category("General")]
        [DisplayName("Title")]
        public string NameProp { get; set; } = "Alpha";

        [Category("General")]
        [DisplayName("Mode")]
        public SampleMode ModeProp { get; set; } = SampleMode.Dynamic;

        [Category("Visual")]
        [DisplayName("Offset")]
        [vector3_attribute]
        public Vector3 VecProp { get; set; } = new(1, 2, 3);

        [Category("Visual")]
        [DisplayName("Tint")]
        public color_rgb ColorProp { get; set; } = new(0.2, 0.4, 0.8, 1.0);

        [Category("Visual")]
        [DisplayName("ColorCurve")]
        public color_curve GradientCurve { get; set; } = new();

        [Category("Visual")]
        [DisplayName("ColorMatrix")]
        public color_matrix GradientMatrix { get; set; }

        [Category("Advanced")]
        [DisplayName("FloatCurve")]
        public float_curve FloatCurveProp { get; set; } = new();

        [Category("Advanced")]
        [DisplayName("Preset")]
        [combo_box_items_attribute("Low", "Medium", "High")]
        public string PresetProp { get; set; } = "Medium";

        [Category("Advanced")]
        [DisplayName("Indices")]
        public List<int> ListProp { get; set; } = new() { 10, 20 };
    }
}
