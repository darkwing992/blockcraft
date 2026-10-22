// @name Wobbly Water
// @description When you are underwater, everything wobbles and turns blue-green.
// @param wobble 1 0 3

vec4 effect(vec4 color, vec2 uv) {
  if (underwater < 0.5) return color;               // only underwater
  vec2 w = vec2(sin(uv.y * 30.0 + time * 3.0), cos(uv.x * 25.0 + time * 2.4)) * 0.004 * wobble;
  vec3 c = pixel(uv + w).rgb;
  c = mix(c, c * vec3(0.6, 1.0, 1.05), 0.5);
  return vec4(c, 1.0);
}
