#pragma once

#include <stdint.h>

namespace WinOS::Graphics
{
	// Standard WinOS colour: 0xAARRGGBB
	using Color = uint32_t;

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
	}; // straight from the efi definition, names edited, same as Shared.h

	struct PixelBitmask {
		uint32_t RMask;
		uint32_t GMask;
		uint32_t BMask;
		uint32_t ResMask;
	}; // straight from the efi definition, names edited, same as Shared.h

	// here we have the whole framebuffer shared to the editor gng :)
	struct Framebuffer
	{
		uint32_t* Address; // changed to uncook the kernel
		uint64_t Size;

		uint32_t Width;
		uint32_t Height;
		uint32_t PixelsPerScanLine;

		PixelFormat PixelFormat;
		PixelBitmask PixelBitmask;
	};
}