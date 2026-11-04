shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled;

// A world's air, as a shell over its sphere: along each line of sight, how much air the light crosses
// (the planet's body taken out), how much sunlight that air holds, and which way it scatters it. Thin
// over the face, a glowing rim at the limb, reddening at the terminator, a bright ring when backlit.
uniform vec3 air = vec3(0.4, 0.6, 1.0);
uniform float density = 1.0;       // how thick the air is: 0 none, 1 an Earth's, more a Venus's
uniform float inner = 0.94;        // the planet's radius over the shell's
uniform vec3 sun = vec3(1.0, 0.0, 0.0);   // from the planet toward its star, in the world

void fragment() {
	vec3 centre = NODE_POSITION_VIEW;
	float ra = length(MODEL_MATRIX[0].xyz) * 0.5;
	float rp = ra * inner;
	vec3 dir = normalize(VERTEX);   // from the camera, at the origin of view space
	float tc = dot(centre, dir);
	float b2 = max(dot(centre, centre) - tc * tc, 0.0);
	if (b2 >= ra * ra) discard;
	float b = sqrt(b2);
	float half_chord = sqrt(ra * ra - b2);
	bool hits = b < rp;
	// The air the ray crosses, in shell thicknesses, weighted to the lowest height it reaches: the air
	// thins with height, so a grazing ray low over the limb crosses the most.
	float thick = ra - rp;
	float path = hits ? half_chord - sqrt(rp * rp - b2) : 2.0 * half_chord;
	float low = hits ? 0.0 : (b - rp) / thick;
	float depth = path / thick * exp(-low * 3.0) * density;
	float haze = 1.0 - exp(-depth * 0.35);

	// Where along the ray the air is lit: its entry over the body, its closest point past the limb.
	vec3 at = (hits ? dir * (tc - half_chord) : dir * tc) - centre;
	vec3 sunv = normalize((VIEW_MATRIX * vec4(sun, 0.0)).xyz);
	float elevation = dot(normalize(at), sunv);
	float lit = smoothstep(-0.3, 0.25, elevation);   // past the terminator the air still holds twilight
	// Low sun light crosses the most air on the way in and comes out red.
	vec3 colour = mix(air, vec3(1.0, 0.45, 0.2), (1.0 - smoothstep(-0.1, 0.35, elevation)) * 0.8);
	// Air scatters forward: with the star behind the planet, the rim lights up.
	float toward = max(dot(dir, sunv), 0.0);
	float phase = 0.7 + 2.5 * pow(toward, 6.0);
	ALBEDO = colour * haze * lit * phase * 0.9;
}
