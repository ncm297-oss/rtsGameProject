// A disc or ring on the ground (M4-VH3, BUG-0430): zone discs and the ability rings. The mesh is the terrain cells under
// it, each cell's own top surface (GroundPatch), so it lies on the ground on both sides of a cliff; UV is the ground offset
// from the centre over the radius, so the circle is cut out here per pixel. A fill out to the rim, and a rim band of
// `rim_share` of the radius at the edge; a ring is a rim with a clear fill (the cells inside it are not even in the mesh).
// M5-V1: `scale` draws the circle at that share of the meshed radius (a resolve flash grows without a mesh rebuild).
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

// No fog include: what is drawn is decided by the view (ZoneDiscs' rule for an enemy storm; the player's own rings).

uniform vec4 fill_color : source_color = vec4(0.0);
uniform vec4 rim_color : source_color = vec4(1.0);
uniform float rim_share = 0.05;
uniform float scale = 1.0;

void fragment() {
	float r = length(UV) / max(scale, 1e-3);
	float aa = max(fwidth(r), 1e-4);
	if (r > 1.0) {
		discard;
	}
	float t = smoothstep(1.0 - rim_share - aa, 1.0 - rim_share, r);
	vec4 c = mix(fill_color, rim_color, t);
	c.a *= 1.0 - smoothstep(1.0 - aa, 1.0, r);
	if (c.a < 0.004) {
		discard;
	}
	ALBEDO = c.rgb;
	ALPHA = c.a;
}
