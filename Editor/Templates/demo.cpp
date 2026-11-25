// WinOS Framework
// Freestanding C++ starter

#define WINOS_EXPORT \
    extern "C" __attribute__((visibility("default")))

WINOS_EXPORT bool WinOS_Initialize() noexcept
{
	return true;
}