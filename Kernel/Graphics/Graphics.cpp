#include "Graphics.h"

namespace WinOS::Graphics
{
	static Framebuffer gFramebuffer{};

	bool Initialize(const BOOT_CONFIG* config)
	{
		// checks
		if (config == nullptr) return false;
		else if (config->BaseAddress == nullptr) return false;
		else if (config->Width == 0 || config->Height == 0) return false;
		else if (config->PixelsPerScanLine < config->Width) return false;

		// checks successful, continue init
		Framebuffer tmp{
			config->BaseAddress,
			config->Width,
			config->Height,
			config->PixelsPerScanLine
		};

		// update def
		gFramebuffer = tmp;

		return true;
	}

	const Framebuffer& GetFramebuffer()
	{
		return gFramebuffer;
	}
}