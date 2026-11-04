shader_type spatial;
render_mode unshaded, shadows_disabled;

// One look per kind of body, grown from noise over the sphere rather than from textures.
uniform int kind = 0;              // 0 rock, 1 giant, 2 moon, 3 star
uniform float seed = 0.0;
uniform vec3 low = vec3(0.1, 0.2, 0.4);
uniform vec3 mid = vec3(0.3, 0.5, 0.25);
uniform vec3 high = vec3(0.8, 0.8, 0.75);
uniform vec3 air = vec3(0.4, 0.6, 1.0);
uniform float sea = 0.5;           // share of a rocky world under water
uniform float clouds = 0.4;
uniform float bands = 7.0;
uniform float spin = 0.0;
uniform vec3 sun = vec3(1.0, 0.0, 0.0);

varying vec3 sphere;
varying vec3 world_normal;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.71, 0.113, 0.419));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p, int octaves) {
	float sum = 0.0, amp = 0.5;
	for (int i = 0; i < octaves; i++) {
		sum += amp * noise(p);
		p = p * 2.03 + vec3(1.7, 9.2, 3.1);
		amp *= 0.5;
	}
	return sum;
}

/// Dark bowls with bright rims, one to a cell.
float craters(vec3 p) {
	vec3 cell = floor(p);
	float shade = 0.0;
	for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) {
		vec3 c = cell + vec3(float(x), float(y), float(z));
		vec3 at = c + vec3(hash(c), hash(c + 3.1), hash(c + 7.7));
		float size = 0.2 + 0.5 * hash(c + 11.3);
		float d = length(p - at) / size;
		if (d < 1.0) shade += smoothstep(1.0, 0.8, d) * (smoothstep(0.7, 0.95, d) * 0.5 - 0.3);
	}
	return shade;
}

void vertex() {
	float a = spin;
	sphere = vec3(VERTEX.x * cos(a) + VERTEX.z * sin(a), VERTEX.y, VERTEX.z * cos(a) - VERTEX.x * sin(a)) * 2.0;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

vec3 rock(vec3 s, float latitude) {
	float h = fbm(s * 2.2, 6);
	float ice = smoothstep(0.72, 0.9, abs(latitude) + (fbm(s * 5.0, 3) - 0.5) * 0.3);
	vec3 c;
	if (h < sea) {
		c = low * (0.6 + 0.8 * h / max(sea, 0.01));
	} else {
		float up = (h - sea) / max(1.0 - sea, 0.01);
		c = mix(mid, high, smoothstep(0.1, 0.5, up)) * (0.75 + 0.5 * noise(s * 16.0));
	}
	c = mix(c, vec3(0.92, 0.95, 1.0), ice);
	float sky = smoothstep(0.52, 0.72, fbm(s * 3.0 + fbm(s * 2.0, 3) * 2.0, 5));
	return mix(c, vec3(0.95), sky * clouds);
}

vec3 giant(vec3 s) {
	float warp = (fbm(s * vec3(2.0, 6.0, 2.0), 5) - 0.5) * 0.5;
	float belt = sin((s.y + warp * 0.35) * bands + seed) * 0.5 + 0.5;
	float fine = sin((s.y + warp * 0.2) * bands * 3.1 + seed * 2.0) * 0.5 + 0.5;
	vec3 c = mix(low, mid, belt);
	c = mix(c, high, smoothstep(0.55, 0.95, fine) * 0.45);
	return c * (0.85 + 0.3 * noise(s * vec3(5.0, 22.0, 5.0)));
}

vec3 moon(vec3 s) {
	float h = fbm(s * 3.0, 5);
	float c = craters(s * 3.5) + craters(s * 8.0 + 4.0) * 0.5;
	return mix(low, high, clamp(h + c, 0.0, 1.0));
}

vec3 star_surface(vec3 s) {
	float cells = fbm(s * 9.0 + fbm(s * 4.0, 3) * 3.0, 5);
	float spots = smoothstep(0.62, 0.72, fbm(s * 2.0, 4));
	return mix(high, mid, smoothstep(0.3, 0.7, cells)) * (1.0 - spots * 0.6);
}

void fragment() {
	vec3 s = normalize(sphere) + seed;
	vec3 surface = normalize(sphere);
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	if (kind == 3) {
		ALBEDO = star_surface(surface + seed) * mix(0.72, 1.0, pow(facing, 0.4));   // limb darkening
	} else {
		vec3 c = kind == 0 ? rock(s, surface.y) : kind == 1 ? giant(surface * 1.0 + vec3(0, 0, 0)) : moon(s);
		float light = clamp(dot(world_normal, normalize(sun)), 0.0, 1.0);
		float day = smoothstep(-0.05, 0.25, dot(world_normal, normalize(sun)));
		ALBEDO = c * (0.012 + 0.72 * pow(light, 0.8) * day);   // the air over it is a shell of its own (atmosphere.gdshader)
	}
}
