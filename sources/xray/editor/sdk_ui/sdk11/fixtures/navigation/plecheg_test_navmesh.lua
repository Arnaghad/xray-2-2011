indices = {
	1,
	5,
	7,
	5,
	6,
	7,
	4,
	6,
	5,
	0,
	2,
	3,
	0,
	4,
	2,
	0,
	6,
	4,
	1,
	3,
	5,
	0,
	3,
	1,
} -- indices

triangle_data = {
	{
		neighbours = {
			6,
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
			2,
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

	{
		neighbours = {
			5,
			1,
			-1,
		}, -- neighbours

		obstructions = {
			0,
			0,
			0,
		}, -- obstructions

		plane = float4( 0, 1, 0, 0 ),
	}, -- 3

	{
		neighbours = {
			4,
			-1,
			7,
		}, -- neighbours

		obstructions = {
			0,
			0,
			0,
		}, -- obstructions

		plane = float4( 0, 1, 0, 0 ),
	}, -- 4

	{
		neighbours = {
			5,
			-1,
			3,
		}, -- neighbours

		obstructions = {
			0,
			0,
			0,
		}, -- obstructions

		plane = float4( 0, 1, 0, 0 ),
	}, -- 5

	{
		neighbours = {
			-1,
			2,
			4,
		}, -- neighbours

		obstructions = {
			0,
			0,
			0,
		}, -- obstructions

		plane = float4( 0, 1, 0, 0 ),
	}, -- 6

	{
		neighbours = {
			7,
			-1,
			0,
		}, -- neighbours

		obstructions = {
			0,
			0,
			0,
		}, -- obstructions

		plane = float4( 0, 1, 0, 0 ),
	}, -- 7

	{
		neighbours = {
			3,
			6,
			-1,
		}, -- neighbours

		obstructions = {
			0,
			0,
			0,
		}, -- obstructions

		plane = float4( 0, 1, 0, 0 ),
	}, -- 8

} -- triangle_data

vertices = {
	float3( 0, 0, 0 ),
	float3( 0, 0, -64 ),
	float3( 10, 0, -10 ),
	float3( 10, 0, -20 ),
	float3( 20, 0, -10 ),
	float3( 20, 0, -20 ),
	float3( 64, 0, 0 ),
	float3( 64, 0, -64 ),
} -- vertices

