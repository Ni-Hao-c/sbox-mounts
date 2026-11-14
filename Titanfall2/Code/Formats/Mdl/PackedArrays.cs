using System.Runtime.InteropServices;
namespace Titanfall2;
// Packed value fields replace native fixed buffers. ByteStream validates POD types.
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedByte3
{
	private byte _0, _1, _2;
	public byte this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedByte64
{
	private byte _0, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14, _15, _16, _17, _18, _19, _20, _21, _22, _23, _24, _25, _26, _27, _28, _29, _30, _31, _32, _33, _34, _35, _36, _37, _38, _39, _40, _41, _42, _43, _44, _45, _46, _47, _48, _49, _50, _51, _52, _53, _54, _55, _56, _57, _58, _59, _60, _61, _62, _63;
	public byte this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			3 => _3,
			4 => _4,
			5 => _5,
			6 => _6,
			7 => _7,
			8 => _8,
			9 => _9,
			10 => _10,
			11 => _11,
			12 => _12,
			13 => _13,
			14 => _14,
			15 => _15,
			16 => _16,
			17 => _17,
			18 => _18,
			19 => _19,
			20 => _20,
			21 => _21,
			22 => _22,
			23 => _23,
			24 => _24,
			25 => _25,
			26 => _26,
			27 => _27,
			28 => _28,
			29 => _29,
			30 => _30,
			31 => _31,
			32 => _32,
			33 => _33,
			34 => _34,
			35 => _35,
			36 => _36,
			37 => _37,
			38 => _38,
			39 => _39,
			40 => _40,
			41 => _41,
			42 => _42,
			43 => _43,
			44 => _44,
			45 => _45,
			46 => _46,
			47 => _47,
			48 => _48,
			49 => _49,
			50 => _50,
			51 => _51,
			52 => _52,
			53 => _53,
			54 => _54,
			55 => _55,
			56 => _56,
			57 => _57,
			58 => _58,
			59 => _59,
			60 => _60,
			61 => _61,
			62 => _62,
			63 => _63,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				case 3: _3 = value; break;
				case 4: _4 = value; break;
				case 5: _5 = value; break;
				case 6: _6 = value; break;
				case 7: _7 = value; break;
				case 8: _8 = value; break;
				case 9: _9 = value; break;
				case 10: _10 = value; break;
				case 11: _11 = value; break;
				case 12: _12 = value; break;
				case 13: _13 = value; break;
				case 14: _14 = value; break;
				case 15: _15 = value; break;
				case 16: _16 = value; break;
				case 17: _17 = value; break;
				case 18: _18 = value; break;
				case 19: _19 = value; break;
				case 20: _20 = value; break;
				case 21: _21 = value; break;
				case 22: _22 = value; break;
				case 23: _23 = value; break;
				case 24: _24 = value; break;
				case 25: _25 = value; break;
				case 26: _26 = value; break;
				case 27: _27 = value; break;
				case 28: _28 = value; break;
				case 29: _29 = value; break;
				case 30: _30 = value; break;
				case 31: _31 = value; break;
				case 32: _32 = value; break;
				case 33: _33 = value; break;
				case 34: _34 = value; break;
				case 35: _35 = value; break;
				case 36: _36 = value; break;
				case 37: _37 = value; break;
				case 38: _38 = value; break;
				case 39: _39 = value; break;
				case 40: _40 = value; break;
				case 41: _41 = value; break;
				case 42: _42 = value; break;
				case 43: _43 = value; break;
				case 44: _44 = value; break;
				case 45: _45 = value; break;
				case 46: _46 = value; break;
				case 47: _47 = value; break;
				case 48: _48 = value; break;
				case 49: _49 = value; break;
				case 50: _50 = value; break;
				case 51: _51 = value; break;
				case 52: _52 = value; break;
				case 53: _53 = value; break;
				case 54: _54 = value; break;
				case 55: _55 = value; break;
				case 56: _56 = value; break;
				case 57: _57 = value; break;
				case 58: _58 = value; break;
				case 59: _59 = value; break;
				case 60: _60 = value; break;
				case 61: _61 = value; break;
				case 62: _62 = value; break;
				case 63: _63 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedFloat2
{
	private float _0, _1;
	public float this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedFloat3
{
	private float _0, _1, _2;
	public float this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedFloat4
{
	private float _0, _1, _2, _3;
	public float this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			3 => _3,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				case 3: _3 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedInt2
{
	private int _0, _1;
	public int this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedInt3
{
	private int _0, _1, _2;
	public int this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedInt4
{
	private int _0, _1, _2, _3;
	public int this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			3 => _3,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				case 3: _3 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedInt6
{
	private int _0, _1, _2, _3, _4, _5;
	public int this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			3 => _3,
			4 => _4,
			5 => _5,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				case 3: _3 = value; break;
				case 4: _4 = value; break;
				case 5: _5 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedInt7
{
	private int _0, _1, _2, _3, _4, _5, _6;
	public int this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			3 => _3,
			4 => _4,
			5 => _5,
			6 => _6,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				case 3: _3 = value; break;
				case 4: _4 = value; break;
				case 5: _5 = value; break;
				case 6: _6 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedInt8
{
	private int _0, _1, _2, _3, _4, _5, _6, _7;
	public int this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			3 => _3,
			4 => _4,
			5 => _5,
			6 => _6,
			7 => _7,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				case 3: _3 = value; break;
				case 4: _4 = value; break;
				case 5: _5 = value; break;
				case 6: _6 = value; break;
				case 7: _7 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedInt60
{
	private int _0, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14, _15, _16, _17, _18, _19, _20, _21, _22, _23, _24, _25, _26, _27, _28, _29, _30, _31, _32, _33, _34, _35, _36, _37, _38, _39, _40, _41, _42, _43, _44, _45, _46, _47, _48, _49, _50, _51, _52, _53, _54, _55, _56, _57, _58, _59;
	public int this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			3 => _3,
			4 => _4,
			5 => _5,
			6 => _6,
			7 => _7,
			8 => _8,
			9 => _9,
			10 => _10,
			11 => _11,
			12 => _12,
			13 => _13,
			14 => _14,
			15 => _15,
			16 => _16,
			17 => _17,
			18 => _18,
			19 => _19,
			20 => _20,
			21 => _21,
			22 => _22,
			23 => _23,
			24 => _24,
			25 => _25,
			26 => _26,
			27 => _27,
			28 => _28,
			29 => _29,
			30 => _30,
			31 => _31,
			32 => _32,
			33 => _33,
			34 => _34,
			35 => _35,
			36 => _36,
			37 => _37,
			38 => _38,
			39 => _39,
			40 => _40,
			41 => _41,
			42 => _42,
			43 => _43,
			44 => _44,
			45 => _45,
			46 => _46,
			47 => _47,
			48 => _48,
			49 => _49,
			50 => _50,
			51 => _51,
			52 => _52,
			53 => _53,
			54 => _54,
			55 => _55,
			56 => _56,
			57 => _57,
			58 => _58,
			59 => _59,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				case 3: _3 = value; break;
				case 4: _4 = value; break;
				case 5: _5 = value; break;
				case 6: _6 = value; break;
				case 7: _7 = value; break;
				case 8: _8 = value; break;
				case 9: _9 = value; break;
				case 10: _10 = value; break;
				case 11: _11 = value; break;
				case 12: _12 = value; break;
				case 13: _13 = value; break;
				case 14: _14 = value; break;
				case 15: _15 = value; break;
				case 16: _16 = value; break;
				case 17: _17 = value; break;
				case 18: _18 = value; break;
				case 19: _19 = value; break;
				case 20: _20 = value; break;
				case 21: _21 = value; break;
				case 22: _22 = value; break;
				case 23: _23 = value; break;
				case 24: _24 = value; break;
				case 25: _25 = value; break;
				case 26: _26 = value; break;
				case 27: _27 = value; break;
				case 28: _28 = value; break;
				case 29: _29 = value; break;
				case 30: _30 = value; break;
				case 31: _31 = value; break;
				case 32: _32 = value; break;
				case 33: _33 = value; break;
				case 34: _34 = value; break;
				case 35: _35 = value; break;
				case 36: _36 = value; break;
				case 37: _37 = value; break;
				case 38: _38 = value; break;
				case 39: _39 = value; break;
				case 40: _40 = value; break;
				case 41: _41 = value; break;
				case 42: _42 = value; break;
				case 43: _43 = value; break;
				case 44: _44 = value; break;
				case 45: _45 = value; break;
				case 46: _46 = value; break;
				case 47: _47 = value; break;
				case 48: _48 = value; break;
				case 49: _49 = value; break;
				case 50: _50 = value; break;
				case 51: _51 = value; break;
				case 52: _52 = value; break;
				case 53: _53 = value; break;
				case 54: _54 = value; break;
				case 55: _55 = value; break;
				case 56: _56 = value; break;
				case 57: _57 = value; break;
				case 58: _58 = value; break;
				case 59: _59 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedSbyte3
{
	private sbyte _0, _1, _2;
	public sbyte this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedShort3
{
	private short _0, _1, _2;
	public short this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct PackedShort4
{
	private short _0, _1, _2, _3;
	public short this[int index]
	{
		readonly get => index switch
		{
			0 => _0,
			1 => _1,
			2 => _2,
			3 => _3,
			_ => throw new ArgumentOutOfRangeException( nameof( index ) )
		};
		set
		{
			switch ( index )
			{
				case 0: _0 = value; break;
				case 1: _1 = value; break;
				case 2: _2 = value; break;
				case 3: _3 = value; break;
				default: throw new ArgumentOutOfRangeException( nameof( index ) );
			}
		}
	}
}
