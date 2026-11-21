#pragma once

#include <WinOS/Graphics/Framebuffer.h>
#include <Shared.h>

namespace WinOS::Graphics
{
	bool Initialize(const BOOT_CONFIG* config);

	const Framebuffer& GetFramebuffer();
}