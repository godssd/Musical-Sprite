// One boundary definition shared by solid fill and its optical emitter.
float RibbonBoundaryX(float y,float4 boundary,float sign,float bow){
 float t=clamp((y-boundary.y)/max(.5,boundary.z),-1,1);
 return boundary.x+sign*bow*(1-t*t);
}
float RibbonProgressSide(float x,float y,float4 boundary,float sign,float bow){
 return (x-RibbonBoundaryX(y,boundary,sign,bow))*sign;
}
