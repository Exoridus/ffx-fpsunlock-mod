using Hexa.NET.ImGui;

namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     A diagnostic ImGui overlay for play-testing: it puts the same clock the log stamps every
///     line with on screen, so a visual defect that leaves no log line of its own can still be
///     matched to the log by eye, and a compact counter view that carries evidence in a screenshot
///     alone.
///
///     Draw-only. It reads counters the other subsystems already maintain for the telemetry line
///     and reuses their formatting rather than duplicating it; it writes nothing back into them,
///     never calls <see cref="advance_this_frame"/>, and holds no state any correction reads.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private long _overlay_marker_count;

    public override void render_imgui()
    {
        if (!_config.TimerOverlay) return;

        if (ImGui.Begin("Fps60 Timer"))
        {
            // Twice the current font size, current font kept (font size base unscaled 0 would mean
            // "use the font's own base size" instead), so this reads at a glance across the room.
            ImGui.PushFont(ImGui.GetFont(), ImGui.GetFontSize() * 2f);
            ImGui.Text($"t={ElapsedSeconds:F1}s");
            ImGui.PopFont();

            // Same "u" format and the same UTC source the logger stamps every line with, so a
            // symptom read off this line can be matched to a log line by eye.
            ImGui.Text($"{TimeProvider.System.GetUtcNow():u}");

            ImGui.Separator();

            if (ImGui.Button("Marker"))
            {
                _overlay_marker_count++;
                _logger.Info($"[Fps60] Marker {_overlay_marker_count} at t={ElapsedSeconds:F1}.");
            }

            ImGui.Separator();
            ImGui.Text($"present={TargetFramerate:F2}fps scale={Scale:F3}");
            ImGui.Text(atel_worker_motion_counts());
            ImGui.Text(idle_sway_counts());
            ImGui.Text(neck_counts());
            ImGui.Text(motion_sequence_counts());
            ImGui.Text(cross_fade_counts());
            ImGui.Text(particle_counts());
            ImGui.Text(buoyancy_counts());
        }

        ImGui.End();
    }
}
