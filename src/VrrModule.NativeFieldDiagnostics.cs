using System.Numerics;
using System.Security.Cryptography;

namespace Fahrenheit.Mods.Vrr;

public unsafe sealed partial class VrrModule
{
    private readonly NativeFieldTraceWindow _nativeFieldTraceWindow = new();

    private static float[] native_field_matrix_words(Matrix4x4 matrix)
        => MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref matrix, 1)).ToArray();

    private void native_field_trace_camera(int viewport, Matrix4x4 canonical, Matrix4x4 rendered)
    {
        var frame = _renderImageFrame.Value;
        if (!_nativeFieldProbeEnabled || _diagnostics?.IsCapturing != true || !_render_frame_enabled || frame is null
            || !_nativeFieldTraceWindow.ShouldRecord(frame.Image.Serial, frame.Admitted)) return;
        diagnostic_event("native_field_camera", new { image_serial = frame.Image.Serial,
            simulation_serial = RenderPassContext.SimulationSerial, history_epoch = RenderPassContext.HistoryEpoch,
            viewport, alpha = _renderCameraAlpha, canonical = native_field_matrix_words(canonical), rendered = native_field_matrix_words(rendered) });
    }

    private void native_field_trace_actor(long image, long simulation, long lifetime, nint instance,
        float alpha, bool admitted, ReadOnlySpan<Matrix4x4> canonical, ReadOnlySpan<Matrix4x4> rendered)
    {
        if (!_nativeFieldProbeEnabled || _diagnostics?.IsCapturing != true
            || !_nativeFieldTraceWindow.ShouldRecord(image, admitted)) return;
        diagnostic_event("native_field_actor", new { image_serial = image, simulation_serial = simulation,
            history_epoch = RenderPassContext.HistoryEpoch, lifetime, instance = (long)instance, alpha,
            matrix_count = rendered.Length,
            canonical_hash = Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(canonical))),
            rendered_hash = Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(rendered))),
            root = native_field_matrix_words(rendered[0]),
            first_bone = rendered.Length > 1 ? native_field_matrix_words(rendered[1]) : null });
    }

    private void native_field_trace_present(int result, uint interval, uint flags, nint swapchain)
    {
        if (!_nativeFieldProbeEnabled || _diagnostics?.IsCapturing != true || (flags & 1) != 0) return;
        var action = _renderImageAction.Value;
        diagnostic_event("native_field_present", new { image_serial = action?.Image?.Serial,
            simulation_serial = action?.ActorReceipt?.Tick.Serial, alpha = action?.ActorReceipt?.Alpha,
            graphics_epoch = action?.AdmissionEpoch, ordinary_complete = action?.OrdinaryComplete,
            admitted = action?.IndependentAdmittedSnapshot, result, interval, flags, swapchain = (long)swapchain });
    }
}
