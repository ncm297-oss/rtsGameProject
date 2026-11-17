// The one material of every unit model (M6-V20, docs/04 "Team color"; M6-V24: the whole figure). Since M6-V24 a model is
// one surface drawing one atlas (tools/art/build_unit.py): a tile per role texture (outfit, skin, head, hair, and the team
// part's brightness) and a tile of the props' flat colours. The atlas alpha is the mask: 0.5 marks a team texel, drawn as
// its brightness times the player colour (so a team part reads in the same per-player colour as a placeholder capsule);
// 0.75 a prop texel (roughness 0.8, as the props' own materials had); 1 any other texel, drawn as it is.
// Each sample is kept inside its tile (half a texel of the mip level read), so filtering never mixes a team tile's texels
// into a skin or a prop and no seam shows when zoomed out. Every face is double-sided (as the imported materials were)
// except a team texel's, which is culled as the team surface was: its back face in the view, its front face in the shadow
// pass (Godot draws a culled material's shadow from the faces turned away from the light: measured, the shield's shadow
// on the Heavy Infantry's legs and the gaps in a Laborer's shadow only match the base that way).
// The render modes are the imported materials' (Godot 4.7's default for a shader of its own is Lambert diffuse; the team
// surface had it, which drew a team part 1-3 levels darker on lit faces than Burley does now).
// Without `use_atlas` (a model file from before M6-V24) the texture is the team texture and every texel is team.
// One ShaderMaterial per model type and player (UnitModels). No fog include: the unit views hide what the player doesn't see.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 team_colour : source_color = vec3(1.0);
uniform float gain = 1.25;
uniform float roughness = 0.9;
uniform float prop_roughness = 0.8;
uniform bool use_atlas = false;
uniform vec2 atlas_grid = vec2(1.0);

void fragment() {
	vec4 texel;
	float team = 1.0;
	float prop = 0.0;
	if (use_atlas) {
		vec2 grid = max(atlas_grid, vec2(1.0));
		vec2 size = vec2(textureSize(albedo_texture, 0));
		vec2 tile = clamp(floor(UV * grid), vec2(0.0), grid - 1.0);
		vec2 dx = dFdx(UV * size);
		vec2 dy = dFdy(UV * size);
		float lod = max(0.0, 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8)));
		vec2 margin = min(0.5 * exp2(lod) / size, 0.5 / grid);
		texel = texture(albedo_texture, clamp(UV, tile / grid + margin, (tile + 1.0) / grid - margin));
		team = step(texel.a, 0.625);
		prop = (1.0 - team) * step(texel.a, 0.875);
	} else {
		texel = texture(albedo_texture, UV);
	}
	// Culled like the team surface: the back face in the view, the front face in the shadow pass.
	if (team > 0.5 && FRONT_FACING == IN_SHADOW_PASS) {
		discard;
	}
	float shade = clamp(texel.r * gain, 0.0, 1.0);
	ALBEDO = mix(texel.rgb, team_colour * shade, team);
	ROUGHNESS = mix(roughness, prop_roughness, prop);
	METALLIC = 0.0;
}
