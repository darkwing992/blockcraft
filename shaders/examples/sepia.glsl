// @name Old Photo
// @description Brown old-photo colours with a soft dark edge.
// @param amount 0.85 0 1
// @param edge 0.5 0 1

vec4 effect(vec4 color, vec2 uv) {
  float l = luma(color.rgb);
  vec3 sepia = vec3(l * 1.1, l * 0.95, l * 0.72);
  vec3 c = mix(color.rgb, sepia, amount);
  c *= 1.0 - smoothstep(0.45, 0.9, distance(uv, vec2(0.5))) * edge;
  return vec4(c, 1.0);
}
