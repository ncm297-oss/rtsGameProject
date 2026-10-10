// A zone disc (M4-V6c): a translucent circle on the ground, one MultiMesh instance per zone the player may see.
// The mesh is a 2 x 2 plane scaled by the zone's radius, so the circle is radius 1 in its own space. The instance colour
// (sRGB) is the fill and its alpha the disc's opacity; INSTANCE_CUSTOM.rgb (sRGB) is the rim colour and its alpha the rim
// band's share of the radius. The rim is drawn more opaque than the fill, and fades with it.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

// No fog include: the disc's visibility rule is ZoneDiscs' (an enemy storm is drawn only where the player sees it).

// sRGB instance colour to linear (as fog.gdshaderinc's fog_srgb_to_linear).
vec3 srgb_to_linear(vec3 c) {
	return mix(pow((c + vec3(0.055)) * (1.0 / 1.055), vec3(2.4)), c * (1.0 / 12.92), lessThan(c, vec3(0.04045)));
}

// The rim's opacity as a multiple of the fill's.
const float RIM_BOOST = 2.2;

varying vec2 local_xz;
varying flat vec4 rim;

void vertex() {
	local_xz = VERTEX.xz;
	rim = INSTANCE_CUSTOM;
}

void fragment() {
	float r = length(local_xz);
	if (r > 1.0) {
		discard;
	}
	float edge = 1.0 - rim.a;
	float t = smoothstep(edge - 0.015, edge, r);
	ALBEDO = mix(srgb_to_linear(COLOR.rgb), srgb_to_linear(rim.rgb), t);
	ALPHA = clamp(COLOR.a * mix(1.0, RIM_BOOST, t), 0.0, 1.0);
}
