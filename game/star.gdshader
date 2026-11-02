shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, shadows_disabled, depth_draw_never;

varying float falloff;

void vertex() {
	// Quad turned to face the camera's position, not laid flat on the screen: a big screen-aligned
	// quad off to the side would swing through the camera as it turns and flash the whole view.
	// INSTANCE_CUSTOM.x = world size, .y = gaussian sharpness.
	vec3 at = MODEL_MATRIX[3].xyz;
	vec3 n = normalize(CAMERA_POSITION_WORLD - at);
	vec3 up = abs(n.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
	vec3 right = normalize(cross(up, n));
	up = cross(n, right);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(right, 0.0), vec4(up, 0.0), vec4(n, 0.0), MODEL_MATRIX[3]);
	VERTEX *= INSTANCE_CUSTOM.x;
	falloff = INSTANCE_CUSTOM.y;
}

void fragment() {
	float d = length(UV - vec2(0.5)) * 2.0;
	float a = exp(-d * d * falloff) * smoothstep(1.0, 0.8, d);
	ALBEDO = COLOR.rgb * COLOR.a * a;
}
