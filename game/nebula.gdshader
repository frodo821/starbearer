shader_type spatial;
render_mode unshaded, blend_add, cull_front, depth_draw_never, shadows_disabled;

// Nebulae as metaballs on the sky. Each nebula is a few puffs; their fields add up, so near puffs
// melt into one irregular cloud with a soft edge. Faint: they are dust and gas, not lamps.
// puffs[i].xyz = direction from the ship, .w = the puff's reach as a chord on the unit sphere
// (above 2.0: the ship is inside it, so it fills the whole sky).
// tints[i].rgb = colour already scaled by the nebula's density.

const int MAX_PUFFS = 160;
uniform int count = 0;
uniform vec4 puffs[MAX_PUFFS];
uniform vec4 tints[MAX_PUFFS];

varying vec3 dir;

void vertex() {
	dir = VERTEX;
}

void fragment() {
	vec3 d = normalize(dir);
	float field = 0.0;
	vec3 colour = vec3(0.0);
	for (int i = 0; i < count; i++) {
		vec4 p = puffs[i];
		float k;
		if (p.w > 2.0) {
			k = 1.0;
		} else {
			float x = length(d - p.xyz) / p.w;
			if (x >= 1.0) continue;
			float s = 1.0 - x * x;
			k = s * s * s;                 // Wyvill: smooth and zero at the reach
		}
		field += k;
		colour += tints[i].rgb * k;
	}
	if (field <= 0.0) discard;
	// Below the threshold only a thin fringe; above it the cloud, a little denser toward the middle.
	float body = smoothstep(0.15, 0.6, field) * (0.7 + 0.3 * min(field, 2.0) / 2.0);
	ALBEDO = colour / field * body;
}
