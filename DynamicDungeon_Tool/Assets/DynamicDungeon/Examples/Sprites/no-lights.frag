#version 330
 
in vec2 v_TexCoord;
uniform sampler2D s_texture;
uniform vec3 v_diffuse;

out vec4 Color;
 
void main()
{
    Color = vec4(v_diffuse, 1) * texture(s_texture, v_TexCoord);
}