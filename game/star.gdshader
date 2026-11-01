shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, shadows_disabled, depth_draw_never, depth_test_disabled;

varying float falloff;

void vertex() {
	// Camera-facing quad. INSTANCE_CUSTOM.x = world size, .y = gaussian sharpness.
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(MAIN_CAM_INV_VIEW_MATRIX[0], MAIN_CAM_INV_VIEW_MATRIX[1], MAIN_CAM_INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	VERTEX *= INSTANCE_CUSTOM.x;
	falloff = INSTANCE_CUSTOM.y;
}

void fragment() {
	float d = length(UV - vec2(0.5)) * 2.0;
	float a = exp(-d * d * falloff) * smoothstep(1.0, 0.8, d);
	ALBEDO = COLOR.rgb * COLOR.a * a;
}
