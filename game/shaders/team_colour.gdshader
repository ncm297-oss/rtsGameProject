// The `team` material of every unit model (M6-V20, docs/04 "Team color"): the outfit's own shading times the player
// colour, so a team part reads in the same per-player colour as a placeholder capsule. The texture is the model's team
// texture, which the import tooling bakes as the outfit's brightness alone, scaled to a mean of 0.8 (tools/art).
// One ShaderMaterial per model type and player (UnitModels). No fog include: the unit views hide what the player doesn't see.
shader_type spatial;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 team_colour : source_color = vec3(1.0);
uniform float gain = 1.25;
uniform float roughness = 0.9;

void fragment() {
	float shade = clamp(texture(albedo_texture, UV).r * gain, 0.0, 1.0);
	ALBEDO = team_colour * shade;
	ROUGHNESS = roughness;
	METALLIC = 0.0;
}
