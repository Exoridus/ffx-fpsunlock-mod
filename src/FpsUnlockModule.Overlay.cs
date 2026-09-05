using Hexa.NET.ImGui;

namespace Fahrenheit.Mods.FpsUnlock;

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
public unsafe sealed partial class FpsUnlockModule
{
    private long _overlay_marker_count;

    public override void render_imgui()
    {
        if (!_config.TimerOverlay) return;

        if (ImGui.Begin("FPS Unlock Timer"))
        {
            // Deliberately drawn at the default font size. Scaling it needs a PushFont overload
            // that nothing in either repository exercises, and this draws from h_present on every
            // presented frame - the one call site where an unproven ImGui call costs a crash rather
            // than a wrong pixel.
            ImGui.Text($"t={ElapsedSeconds:F1}s");

            // Same "u" format and the same UTC source the logger stamps every line with, so a
            // symptom read off this line can be matched to a log line by eye.
            ImGui.Text($"{TimeProvider.System.GetUtcNow():u}");

            ImGui.Separator();

            if (ImGui.Button("Marker"))
            {
                _overlay_marker_count++;

                // The whole counter set, not just the time. A marker exists to capture the state at
                // the instant a defect was seen; the periodic sample is up to five seconds away from
                // that instant in either direction, which is long enough for the counters that move
                // per frame to say nothing about it.
                _logger.Info($"[FpsUnlock] MARKER {_overlay_marker_count} at t={ElapsedSeconds:F1}, " +
                             $"scale={Scale:F3}, {counter_snapshot()}");
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
