#include "Graphics.h"

namespace WinOS::Graphics
{
	static Framebuffer gFramebuffer{};

	bool Initialize(const BOOT_CONFIG* config)
	{
		// some checks
		if (config == nullptr) return false;
		else if (config->GOPExports.FBAddress == 0) return false;
		else if (config->GOPExports.HorizontalResolution == 0 || config->GOPExports.VerticalResolution == 0) return false;
		else if (config->GOPExports.PixelsPerScanLine < config->GOPExports.HorizontalResolution) return false;

		uint64_t requiredSize =
			(uint64_t)config->GOPExports.PixelsPerScanLine *
			config->GOPExports.VerticalResolution * 4; // Frame Buffer size = Pixels Per Scan Line * Height Pixels * 4

		if (config->GOPExports.FBSize < requiredSize)
			return false;

		// pixel format validation
		switch (config->GOPExports.PixelFormat)
		{
		case RGB8Bit:
		case BGR8Bit:
		case BitMask:
			break;

		case BltOnly:
		case FormatMax:
		default:
			return false;
		}

		// a bit of safety, coz we are converting stuff
		PixelFormat format;

		switch (config->GOPExports.PixelFormat)
		{
		case RGB8Bit:
			format = PixelFormat::RGB8Bit;
			break;

		case BGR8Bit:
			format = PixelFormat::BGR8Bit;
			break;

		case BitMask:
			format = PixelFormat::BitMask;
			break;

		default:
			return false;
		}

		PixelBitmask bitmask{
			config->GOPExports.PixelBitmask.RMask,
			config->GOPExports.PixelBitmask.GMask,
			config->GOPExports.PixelBitmask.BMask,
			config->GOPExports.PixelBitmask.ResMask
		};

		if (format == PixelFormat::BitMask)
		{
			if (bitmask.RMask == 0 ||
				bitmask.GMask == 0 ||
				bitmask.BMask == 0)
				return false;

			if ((bitmask.RMask & bitmask.GMask) ||
				(bitmask.RMask & bitmask.BMask) ||
				(bitmask.GMask & bitmask.BMask) ||
				(bitmask.ResMask & (bitmask.RMask | bitmask.GMask | bitmask.BMask)))
				return false;
		}

		// checks successful, continue init
		Framebuffer tmp{
			(uint32_t*)config->GOPExports.FBAddress,
			config->GOPExports.FBSize,
			config->GOPExports.HorizontalResolution,
			config->GOPExports.VerticalResolution,
			config->GOPExports.PixelsPerScanLine,
			format
		};

		// update def
		gFramebuffer = tmp;

		return true;
	}

	const Framebuffer& GetFramebuffer()
	{
		return gFramebuffer;
	}

	static uint32_t PackChannel(uint8_t channel, uint32_t mask)
	{
		if (mask == 0) return 0;

		uint32_t shift = 0;
		while (((mask >> shift) & 1) == 0)
			++shift;

		uint32_t maxValue = mask >> shift;
		uint32_t value = (channel * maxValue + 127) / 255;

		return (value << shift) & mask;
	}

	static uint32_t ConvertColor(Color color)
	{
		uint8_t r = (color >> 16) & 0xFF;
		uint8_t g = (color >> 8) & 0xFF;
		uint8_t b = color & 0xFF;

		switch (gFramebuffer.PixelFormat)
		{
		case PixelFormat::RGB8Bit:
			return (r << 0) | (g << 8) | (b << 16);

		case PixelFormat::BGR8Bit:
			return (b << 0) | (g << 8) | (r << 16);

		case PixelFormat::BitMask:
			return
				PackChannel(r, gFramebuffer.PixelBitmask.RMask) |
				PackChannel(g, gFramebuffer.PixelBitmask.GMask) |
				PackChannel(b, gFramebuffer.PixelBitmask.BMask);

		default:
			return 0;
		}
	}

	bool WritePixel(uint32_t x, uint32_t y, Color color)
	{
		// Ensure framebuffer is initialized
		if (gFramebuffer.Address == nullptr)
			return false;

		// Bounds check
		if (x >= gFramebuffer.Width || y >= gFramebuffer.Height)
			return false;

		// Calculate pixel offset
		uint64_t offset =
			(uint64_t)y * gFramebuffer.PixelsPerScanLine + x;

		// Prevent out-of-bounds memory access
		if (offset >= gFramebuffer.Size / sizeof(uint32_t))
			return false;

		// Write converted pixel
		gFramebuffer.Address[offset] = ConvertColor(color);

		return true;
	}
}