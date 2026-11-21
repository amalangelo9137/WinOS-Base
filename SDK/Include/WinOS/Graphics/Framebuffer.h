#pragma once

#include <stdint.h>

namespace WinOS::Graphics
{
	enum class PixelFormat : uint8_t // straight from the EFI Definition
	{
		// A pixel is 32 bits; Definition from the physical frame buffer
		// byte 0 = red intensity, byte 1 = green intensity, byte 2 = blue intensity, byte 3 = reserved
		RGB8Bit,
		// A pixel is 32 bits; Definition from the physical frame buffer
		// byte 0 = blue intensity, byte 1 = green intensity, byte 2 = red intensity, byte 3 = reserved
		BGR8Bit,
		// Pixel definition of the physical frame buffer.
		BitMask,
		/// Physical frame buffer not supported.
		BltOnly,
		// Maximum allowed value
		FormatMax
	};

	// here we have the whole framebuffer shared to the editor gng :)
	struct Framebuffer
	{
		uint32_t* Address;

		uint32_t Width;
		uint32_t Height;
		uint32_t PixelsPerScanLine;

		PixelFormat PixelFormat;
	};
}