#pragma once
#include <stdint.h>
#include <stdbool.h>

typedef enum {
	AssetTypeUnknown = 0,
	AssetTypeBitmap = 1,
	AssetTypeGif = 2,
	AssetTypeAppPackage = 3,
	AssetTypePsfFont = 4,
} ASSET_TYPE;

// Represents a single file loaded into RAM by the EFI Index
typedef struct {
	char     Name[32];
	void* Address;
	uint32_t Size;
	uint32_t Type;
} ASSET_ENTRY;

typedef struct {
	uint32_t     Count;
	ASSET_ENTRY* Entries;
} ASSET_TABLE;

typedef struct {
	int X, Y;
	bool Left, Right;
} MOUSE_STATE;

typedef struct {
	unsigned char Magic[2];     // 0x36, 0x04
	unsigned char Mode;         // 0 = 256 chars, 1 = 512 chars
	unsigned char CharSize;     // Height of character in pixels
} PSF1_HEADER;

typedef enum {
	RGB8Bit,
	BGR8Bit,
	BitMask,
	BltOnly,
	FormatMax
} BOOT_PIXEL_FORMAT; // straight from the efi definition, names edited, Shared.h

typedef struct {
	uint32_t RMask;
	uint32_t GMask;
	uint32_t BMask;
	uint32_t ResMask;
} BOOT_PIXEL_BITMASK; // straight from the efi definition, names edited, Shared.h

typedef struct {
	// base
	uint64_t FBAddress; // where the framebuffer is located.
	uint64_t FBSize; // size of the framebuffer

	// resolution
	uint32_t HorizontalResolution;
	uint32_t VerticalResolution;
	uint32_t PixelsPerScanLine;

	// advanced
	BOOT_PIXEL_FORMAT PixelFormat;
	BOOT_PIXEL_BITMASK PixelBitmask;
} GOP_EXPORTS; // snapshot of the EFI GOP

// The ultimate payload handed to the Kernel
typedef struct {
	// graphics related sht
	GOP_EXPORTS GOPExports;

	MOUSE_STATE* Mouse;
	PSF1_HEADER* Font;
	ASSET_TABLE* Assets;         // The dynamic Index Table

	// Pure pointers to avoid breaking compiler cross-compatibility
	void* MemMap;
	uint64_t  MemMapSize;
	uint64_t  MemMapDescriptorSize;
} BOOT_CONFIG;

// ============================================================
// WinOS Dynamic Framework Library (.wdll)
// ============================================================

#define WDLL_MAGIC "WDLL"
#define WDLL_FORMAT_VERSION 1

typedef enum {
	WDLL_ARCH_UNKNOWN = 0,
	WDLL_ARCH_X86_64 = 1
} WDLL_ARCHITECTURE;

typedef struct {
	uint8_t Magic[4];              // "WDLL"
	uint16_t FormatVersion;        // WDLL_FORMAT_VERSION
	uint16_t HeaderSize;

	uint32_t Architecture;         // WDLL_ARCHITECTURE
	uint32_t Flags;

	uint64_t ManifestOffset;
	uint64_t ManifestSize;

	uint64_t PayloadOffset;
	uint64_t PayloadSize;
} WDLL_HEADER;