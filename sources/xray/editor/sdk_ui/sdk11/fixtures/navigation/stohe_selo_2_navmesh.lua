indices = {
	0,
	1,
	2,
	1,
	3,
	2,
} -- indices

triangle_data = {
	{
		neighbours = {
			-1,
			1,
			-1,
		}, -- neighbours

		obstructions = {
			0,
			0,
			0,
		}, -- obstructions

		plane = float4( 0, 1, 0, 0 ),
	}, -- 1

	{
		neighbours = {
			-1,
			-1,
			0,
		}, -- neighbours

		obstructions = {
			0,
			0,
			0,
		}, -- obstructions

		plane = float4( 0, 1, 0, 0 ),
	}, -- 2

} -- triangle_data

vertices = {
	float3( 0, 0, -64 ),
	float3( 0, 0, 64 ),
	float3( 128, 0, -64 ),
	float3( 128, 0, 64 ),
} -- vertices

