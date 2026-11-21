#define PROJ L"WinOS-EFI"
#define FILE L"main.c"

// my own implementation attempt of WinOS.
// Welcome to WinOS (not WinOS-Dev)!
#include <Guid/FileInfo.h>
#include <IndustryStandard/PeImage.h>
#include <Library/UefiBootServicesTableLib.h>
#include <Library/UefiLib.h>
#include <Protocol/GraphicsOutput.h>
#include <Protocol/LoadedImage.h>
#include <Protocol/SimpleFileSystem.h>
#include <intrin.h>

#include "Shared.h"
#include <Base.h>
#include <Library/BaseLib.h>
#include <ProcessorBind.h>
#include <Uefi/UefiBaseType.h>
#include <Uefi/UefiMultiPhase.h>
#include <Uefi/UefiSpec.h>
#include <stdint.h>

#include "Debug.h"

extern CONST UINT32 _gUefiDriverRevision = 0;
CHAR8* gEfiCallerBaseName = "WinOS";

#define DEBUG_FILE L"main.c"

#define INFO L"i"
#define WARN L"!"
#define ERROR L"X"

BOOT_CONFIG BootConfig;

static BOOLEAN EndsWithIgnoreCase(const char* value, const char* suffix) {
	if (value == NULL || suffix == NULL) {
		return FALSE;
	}

	UINTN valueLength = AsciiStrLen(value);
	UINTN suffixLength = AsciiStrLen(suffix);
	if (suffixLength > valueLength) {
		return FALSE;
	}

	return AsciiStriCmp(value + (valueLength - suffixLength), suffix) == 0;
}

static UINT32 InferAssetTypeFromPath(const char* path) {
	if (path == NULL) {
		return AssetTypeUnknown;
	}

	if (EndsWithIgnoreCase(path, ".bmp")) return AssetTypeBitmap;
	if (EndsWithIgnoreCase(path, ".gif")) return AssetTypeGif;
	if (EndsWithIgnoreCase(path, ".wdexe")) return AssetTypeAppPackage;
	if (EndsWithIgnoreCase(path, ".psf")) return AssetTypePsfFont;
	return AssetTypeUnknown;
}

static UINT32 DetectAssetType(const void* buffer, UINTN size) {
	if (buffer == NULL || size < 4) {
		return AssetTypeUnknown;
	}

	const UINT8* bytes = (const UINT8*)buffer;
	if (size >= 2 && bytes[0] == 'B' && bytes[1] == 'M') return AssetTypeBitmap;
	if (size >= 4 && bytes[0] == 'W' && bytes[1] == 'D' && bytes[2] == 'X' && bytes[3] == 'E') return AssetTypeAppPackage;
	if (size >= 3 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F') return AssetTypeGif;
	if (size >= sizeof(PSF1_HEADER)) {
		const PSF1_HEADER* font = (const PSF1_HEADER*)buffer;
		if (font->Magic[0] == 0x36 && font->Magic[1] == 0x04) {
			return AssetTypePsfFont;
		}
	}

	return AssetTypeUnknown;
}

/// <summary>
/// Get a file from the loaded file system.
/// </summary>
/// <param name="Directory">Pointer to the root dir (normally)</param>
/// <param name="Path">File path</param>
/// <param name="LoadedSize">Size of obtained file (use & to write to variable, eg. &LoadedSize)</param>
/// <returns>The file location where it is loaded to RAM</returns>
static void* LoadFileIntoRAM(EFI_FILE* Directory, CHAR16* Path, UINTN* LoadedSize)
{
	EFI_STATUS Status;
	EFI_FILE* LoadedFile;

	//
	// Open file
	//
	Status = Directory->Open(
		Directory,
		&LoadedFile,
		Path,
		EFI_FILE_MODE_READ,
		0
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to open file\n");
		DEBUG_PRINT(ERROR, L"    Path: %s\n", Path);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);
		return NULL;
	}

	DEBUG_PRINT(INFO, L"Opened file\n");
	DEBUG_PRINT(INFO, L"    Path: %s\n", Path);

	//
	// Allocate buffer for EFI_FILE_INFO
	//
	UINTN FileInfoSize = sizeof(EFI_FILE_INFO) + 1024;
	EFI_FILE_INFO* FileInfo;

	Status = gBS->AllocatePool(
		EfiLoaderData,
		FileInfoSize,
		(void**)&FileInfo
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to allocate file info buffer\n");
		DEBUG_PRINT(ERROR, L"    Path: %s\n", Path);
		DEBUG_PRINT(ERROR, L"    Requested size: %u bytes\n", FileInfoSize);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(WARN, L"Failed to close file after error\n");
			DEBUG_PRINT(WARN, L"    Status: %r\n", Status);
		}

		return NULL;
	}

	//
	// Get file information
	//
	Status = LoadedFile->GetInfo(
		LoadedFile,
		&gEfiFileInfoGuid,
		&FileInfoSize,
		(void**)FileInfo
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to get file information\n");
		DEBUG_PRINT(ERROR, L"    Path: %s\n", Path);
		DEBUG_PRINT(ERROR, L"    File info buffer size: %u bytes\n", FileInfoSize);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(WARN, L"Failed to close file after error\n");
			DEBUG_PRINT(WARN, L"    Status: %r\n", Status);
		}

		Status = gBS->FreePool(FileInfo);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(WARN, L"Failed to free file info buffer\n");
			DEBUG_PRINT(WARN, L"    Status: %r\n", Status);
		}

		return NULL;
	}

	UINTN FileSize = FileInfo->FileSize;

	//
	// FileInfo is no longer needed.
	//
	Status = gBS->FreePool(FileInfo);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(WARN, L"Failed to free file info buffer\n");
		DEBUG_PRINT(WARN, L"    Path: %s\n", Path);
		DEBUG_PRINT(WARN, L"    Status: %r\n", Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(WARN, L"Failed to close file\n");
			DEBUG_PRINT(WARN, L"    Status: %r\n", Status);
		}

		return NULL;
	}

	DEBUG_PRINT(INFO, L"File information retrieved\n");
	DEBUG_PRINT(INFO, L"    Path: %s\n", Path);
	DEBUG_PRINT(INFO, L"    File size: %u bytes\n", FileSize);

	//
	// Empty files are not useful to the caller.
	//
	if (FileSize == 0)
	{
		DEBUG_PRINT(WARN, L"File is empty\n");
		DEBUG_PRINT(WARN, L"    Path: %s\n", Path);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(WARN, L"Failed to close empty file\n");
			DEBUG_PRINT(WARN, L"    Status: %r\n", Status);
		}

		return NULL;
	}

	//
	// Calculate required number of pages.
	//
	UINTN NumPages = (FileSize + 4095) / 4096;

	DEBUG_PRINT(INFO, L"Calculated memory requirements\n");
	DEBUG_PRINT(INFO, L"    File size: %u bytes\n", FileSize);
	DEBUG_PRINT(INFO, L"    Page size: 4096 bytes\n");
	DEBUG_PRINT(INFO, L"    Pages required: %u\n", NumPages);

	//
	// Allocate pages for file contents.
	//
	EFI_PHYSICAL_ADDRESS FileBufferAddr;

	Status = gBS->AllocatePages(
		AllocateAnyPages,
		EfiLoaderData,
		NumPages,
		&FileBufferAddr
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to allocate file pages\n");
		DEBUG_PRINT(ERROR, L"    Path: %s\n", Path);
		DEBUG_PRINT(ERROR, L"    File size: %u bytes\n", FileSize);
		DEBUG_PRINT(ERROR, L"    Pages requested: %u\n", NumPages);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(WARN, L"[!] LoadFileIntoRAM: Failed to close file after error\n");
			DEBUG_PRINT(WARN, L"    Status: %r\n", Status);
		}

		return NULL;
	}

	void* FileBuffer = (void*)FileBufferAddr;

	DEBUG_PRINT(INFO, L"Allocated file buffer\n");
	DEBUG_PRINT(INFO, L"    Address: %p\n", FileBuffer);
	DEBUG_PRINT(INFO, L"    Pages: %u\n", NumPages);
	DEBUG_PRINT(INFO, L"    Capacity: %u bytes\n", NumPages * 4096);

	//
	// Preserve the requested size because Read() modifies FileSize.
	//
	UINTN ExpectedFileSize = FileSize;

	//
	// Read file into allocated memory.
	//
	Status = LoadedFile->Read(
		LoadedFile,
		&FileSize,
		FileBuffer
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to read file;\n	Path: %s,\n	Buffer: %p,\n	Expected size: %u bytes,\n	Bytes read: %u bytes,\n	Pages allocated: %u,\n	Status: %r",
			Path, FileBuffer, ExpectedFileSize, FileSize, NumPages, Status);

		Status = LoadedFile->Close(LoadedFile);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(ERROR, L"Failed to close file after read error;\n	Status: %r",
				Status);
		}

		//
		// IMPORTANT:
		// FileBuffer was allocated with AllocatePages().
		// This allocation must be released here on failure.
		//
		Status = gBS->FreePages(FileBufferAddr, NumPages);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(ERROR, L"Failed to free file buffer;\n	Address: %p,\n	Pages: %u,\n	Status: %r",
				FileBuffer, NumPages, Status);
		}

		return NULL;
	}

	//
	// File is no longer needed.
	//
	Status = LoadedFile->Close(LoadedFile);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"File loaded, but Close() failed;\n	Path: %s,\n	Status: %r",
			Path, Status);

		//
		// The file contents were successfully loaded, so the buffer
		// still belongs to the caller.
		//
	}

	//
	// Report actual loaded size to caller.
	//
	if (LoadedSize != NULL)
	{
		*LoadedSize = FileSize;
	}

	DEBUG_PRINT(INFO, L"File loaded successfully;\n	Path: %s,\n	Buffer: %p,\n	Expected Size: %u bytes,\n	Loaded Size: %u bytes,\n	Pages allocated: %u",
		Path, FileBuffer, ExpectedFileSize, FileSize, NumPages);

	if (LoadedSize != NULL)
	{
		DEBUG_PRINT(INFO, L"	LoadedSize output: %u\n", *LoadedSize);
	}
	else
	{
		DEBUG_PRINT(INFO, L"	LoadedSize output: NULL\n");
	}

	return FileBuffer;
}

/// <summary>
/// Browses index.wfs to check for files and stuff. Derived straight from WinOS-Dev, and may be deprecated soon.
/// </summary>
/// <param name="IndexRaw">Raw Index</param>
/// <param name="Table">Asset table</param>
/// <param name="RootDir">Root directory</param>
static void CheckIndexFile(void* IndexRaw, ASSET_TABLE* Table, EFI_FILE* RootDir)
{
	if (IndexRaw != NULL) {
		char* IndexPtr = (char*)IndexRaw;
		while (*IndexPtr != '\0' && Table->Count < 50) {
			while (*IndexPtr == '\r' || *IndexPtr == '\n' || *IndexPtr == ' ') IndexPtr++;
			if (*IndexPtr == '\0') break;

			char* NameStart = IndexPtr;
			while (*IndexPtr != ':' && *IndexPtr != '\0') IndexPtr++;
			if (*IndexPtr == '\0') break;
			*IndexPtr = '\0';
			IndexPtr++;

			char* PathStart = IndexPtr;
			while (*IndexPtr != '\r' && *IndexPtr != '\n' && *IndexPtr != '\0') IndexPtr++;
			if (*IndexPtr != '\0') { *IndexPtr = '\0'; IndexPtr++; }

			CHAR16 UPath[128];
			UPath[0] = L'\\';
			int uIdx = 1, pIdx = 0;
			while (PathStart[pIdx] == ' ' || PathStart[pIdx] == '\\') pIdx++;
			while (PathStart[pIdx] != '\0' && uIdx < 127) {
				UPath[uIdx++] = (CHAR16)PathStart[pIdx++];
			}
			UPath[uIdx] = L'\0';

			UINTN AssetSize = 0;
			void* AssetAddr = LoadFileIntoRAM(RootDir, UPath, &AssetSize);
			if (AssetAddr) {
				UINT32 expectedType = InferAssetTypeFromPath(PathStart);
				UINT32 actualType = DetectAssetType(AssetAddr, AssetSize);
				if (expectedType != AssetTypeUnknown &&
					actualType != AssetTypeUnknown &&
					expectedType != actualType) {
					DEBUG_PRINT(WARN, L"Skipping mismatched asset type : % a\n", NameStart);
					continue;
				}

				AsciiStrCpyS(Table->Entries[Table->Count].Name, 32, NameStart);
				Table->Entries[Table->Count].Address = AssetAddr;
				Table->Entries[Table->Count].Size = (UINT32)AssetSize;
				Table->Entries[Table->Count].Type = actualType;
				Table->Count++;
			}
		}
	}
}

/// <summary>
/// Build boot configurations (init).
/// </summary>
void BuildBootConfig(ASSET_TABLE* Table, EFI_GRAPHICS_OUTPUT_PROTOCOL* GOP) {
	DEBUG_PRINT(INFO, L"Building BootConfig...");

	BootConfig.Assets = Table;

	BootConfig.GOPExports.FBAddress = GOP->Mode->FrameBufferBase;
	DEBUG_PRINT(INFO, L"Framebuffer Address: 0x%lx", BootConfig.GOPExports.FBAddress);
	BootConfig.GOPExports.FBSize = GOP->Mode->FrameBufferSize;
	DEBUG_PRINT(INFO, L"Framebuffer Size: %lu bytes", BootConfig.GOPExports.FBSize);

	BootConfig.GOPExports.HorizontalResolution = GOP->Mode->Info->HorizontalResolution;
	DEBUG_PRINT(INFO, L"Horizontal Resolution: %u px", BootConfig.GOPExports.HorizontalResolution);
	BootConfig.GOPExports.VerticalResolution = GOP->Mode->Info->VerticalResolution;
	DEBUG_PRINT(INFO, L"Vertical Resolution: %u px", BootConfig.GOPExports.VerticalResolution);
	BootConfig.GOPExports.PixelsPerScanLine = GOP->Mode->Info->PixelsPerScanLine;
	DEBUG_PRINT(INFO, L"Pixels Per Scan Line: %u px", BootConfig.GOPExports.PixelsPerScanLine);

	BootConfig.GOPExports.PixelBitmask.RMask =
		GOP->Mode->Info->PixelInformation.RedMask;

	BootConfig.GOPExports.PixelBitmask.GMask =
		GOP->Mode->Info->PixelInformation.GreenMask;

	BootConfig.GOPExports.PixelBitmask.BMask =
		GOP->Mode->Info->PixelInformation.BlueMask;

	BootConfig.GOPExports.PixelBitmask.ResMask =
		GOP->Mode->Info->PixelInformation.ReservedMask;

	switch (GOP->Mode->Info->PixelFormat) {
	case PixelRedGreenBlueReserved8BitPerColor:
		BootConfig.GOPExports.PixelFormat = RGB8Bit;
		break;

	case PixelBlueGreenRedReserved8BitPerColor:
		BootConfig.GOPExports.PixelFormat = BGR8Bit;
		break;

	case PixelBitMask:
		BootConfig.GOPExports.PixelFormat = BitMask;
		break;

	case PixelBltOnly:
		BootConfig.GOPExports.PixelFormat = BltOnly;
		break;

	default:
		BootConfig.GOPExports.PixelFormat = FormatMax;
		break;
	}

	DEBUG_PRINT(INFO, L"Succesfully built BootConfig!");
}

EFI_STATUS EFIAPI UefiMain(IN EFI_HANDLE ImageHandle, IN EFI_SYSTEM_TABLE* SystemTable)
{
	EFI_STATUS Status;

	gST->ConOut->ClearScreen(gST->ConOut);

	DEBUG_PRINT(INFO, L"==> WinOS UEFI Loader v1.4 Patch 1 (Debug Edition) <==\n");

	//
	// ------------------------------------------------------------
	// 1. Locate Graphics Output Protocol
	// ------------------------------------------------------------
	//

	EFI_GRAPHICS_OUTPUT_PROTOCOL* GOP = NULL;

	Status = gBS->LocateProtocol(
		&gEfiGraphicsOutputProtocolGuid,
		NULL,
		(void**)&GOP
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to locate Graphics Output Protocol\n");
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"GOP located\n");
	DEBUG_PRINT(INFO, L"    GOP: %p\n", GOP);
	DEBUG_PRINT(INFO, L"    FrameBuffer: %p\n",
		(void*)GOP->Mode->FrameBufferBase);
	DEBUG_PRINT(INFO, L"    Resolution: %u x %u\n",
		GOP->Mode->Info->HorizontalResolution,
		GOP->Mode->Info->VerticalResolution);
	DEBUG_PRINT(INFO, L"    PixelsPerScanLine: %u\n",
		GOP->Mode->Info->PixelsPerScanLine);

	//
	// ------------------------------------------------------------
	// 2. Get Loaded Image Protocol
	// ------------------------------------------------------------
	//

	EFI_LOADED_IMAGE_PROTOCOL* LoadedImage = NULL;

	Status = gBS->HandleProtocol(
		ImageHandle,
		&gEfiLoadedImageProtocolGuid,
		(void**)&LoadedImage
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to get Loaded Image Protocol\n");
		DEBUG_PRINT(ERROR, L"    ImageHandle: %p\n", ImageHandle);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"Loaded Image Protocol located\n");
	DEBUG_PRINT(INFO, L"    LoadedImage: %p\n", LoadedImage);
	DEBUG_PRINT(INFO, L"    DeviceHandle: %p\n", LoadedImage->DeviceHandle);

	//
	// ------------------------------------------------------------
	// 3. Get Simple File System Protocol
	// ------------------------------------------------------------
	//

	EFI_SIMPLE_FILE_SYSTEM_PROTOCOL* FileSystem = NULL;

	Status = gBS->HandleProtocol(
		LoadedImage->DeviceHandle,
		&gEfiSimpleFileSystemProtocolGuid,
		(void**)&FileSystem
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to get Simple File System Protocol\n");
		DEBUG_PRINT(ERROR, L"    DeviceHandle: %p\n", LoadedImage->DeviceHandle);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"Simple File System Protocol located\n");
	DEBUG_PRINT(INFO, L"    FileSystem: %p\n", FileSystem);

	//
	// ------------------------------------------------------------
	// 4. Open filesystem volume
	// ------------------------------------------------------------
	//

	EFI_FILE* RootDir = NULL;

	Status = FileSystem->OpenVolume(
		FileSystem,
		&RootDir
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to open filesystem volume\n");
		DEBUG_PRINT(ERROR, L"    FileSystem: %p\n", FileSystem);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"Filesystem volume opened\n");
	DEBUG_PRINT(INFO, L"    RootDir: %p\n", RootDir);

	//
	// ------------------------------------------------------------
	// 5. Reset root directory position
	// ------------------------------------------------------------
	//

	Status = RootDir->SetPosition(
		RootDir,
		0
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(INFO, L"Failed to reset root directory position\n");
		DEBUG_PRINT(INFO, L"    RootDir: %p\n", RootDir);
		DEBUG_PRINT(INFO, L"    Position: 0\n");
		DEBUG_PRINT(INFO, L"    Status: %r\n", Status);

		RootDir->Close(RootDir);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"Root directory position reset\n");

	//
	// ------------------------------------------------------------
	// 6. Load index.wfs
	// ------------------------------------------------------------
	//

	DEBUG_PRINT(INFO, L"Loading index file\n");
	DEBUG_PRINT(INFO, L"    Path: \\\\index.wfs\n");

	void* IndexRaw = LoadFileIntoRAM(
		RootDir,
		L"\\index.wfs",
		NULL
	);

	//
	// index.wfs is currently optional.
	// CheckIndexFile() already accepts NULL.
	//
	if (IndexRaw == NULL)
	{
		DEBUG_PRINT(WARN, L"index.wfs was not loaded\n");
		DEBUG_PRINT(WARN, L"    Continuing without indexed assets\n");
	}
	else
	{
		DEBUG_PRINT(INFO, L"index.wfs loaded\n");
		DEBUG_PRINT(INFO, L"    Address: %p\n", IndexRaw);
	}

	//
	// ------------------------------------------------------------
	// 7. Allocate ASSET_TABLE
	// ------------------------------------------------------------
	//

	ASSET_TABLE* Table = NULL;

	Status = gBS->AllocatePool(
		EfiLoaderData,
		sizeof(ASSET_TABLE),
		(void**)&Table
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to allocate ASSET_TABLE\n");
		DEBUG_PRINT(ERROR, L"    Requested size: %u bytes\n",
			sizeof(ASSET_TABLE));
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		if (IndexRaw != NULL)
		{
			//
			// IndexRaw ownership belongs to us here.
			//
			// The actual page count is not available because
			// this call requested LoadedSize == NULL.
			//
			// We therefore leave it alone for now.
		}

		RootDir->Close(RootDir);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"ASSET_TABLE allocated\n");
	DEBUG_PRINT(INFO, L"    Address: %p\n", Table);
	DEBUG_PRINT(INFO, L"    Size: %u bytes\n", sizeof(ASSET_TABLE));

	//
	// ------------------------------------------------------------
	// 8. Allocate asset entries
	// ------------------------------------------------------------
	//

	UINTN MaxAssetEntries = 50;
	UINTN AssetEntriesSize =
		sizeof(ASSET_ENTRY) * MaxAssetEntries;

	Status = gBS->AllocatePool(
		EfiLoaderData,
		AssetEntriesSize,
		(void**)&Table->Entries
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to allocate asset entries\n");
		DEBUG_PRINT(ERROR, L"    Maximum entries: %u\n", MaxAssetEntries);
		DEBUG_PRINT(ERROR, L"    Requested size: %u bytes\n", AssetEntriesSize);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		gBS->FreePool(Table);
		RootDir->Close(RootDir);

		while (TRUE)
			__halt();
	}

	Table->Count = 0;

	DEBUG_PRINT(INFO, L"Asset entries allocated\n");
	DEBUG_PRINT(INFO, L"    Address: %p\n", Table->Entries);
	DEBUG_PRINT(INFO, L"    Maximum entries: %u\n", MaxAssetEntries);
	DEBUG_PRINT(INFO, L"    Buffer size: %u bytes\n", AssetEntriesSize);

	//
	// ------------------------------------------------------------
	// 9. Parse index
	// ------------------------------------------------------------
	//

	DEBUG_PRINT(INFO, L"Parsing asset index...\n");

	CheckIndexFile(
		IndexRaw,
		Table,
		RootDir
	);

	DEBUG_PRINT(INFO, L"Asset index processing complete\n");
	DEBUG_PRINT(INFO, L"    Asset count: %u\n", Table->Count);

	//
	// ------------------------------------------------------------
	// 10. Build BootConfig
	// ------------------------------------------------------------
	//

	DEBUG_PRINT(INFO, L"Building BootConfig...\n");

	BuildBootConfig(
		Table,
		GOP
	);

	DEBUG_PRINT(INFO, L"BootConfig ready\n");
	DEBUG_PRINT(INFO, L"    Assets: %p\n", BootConfig.Assets);
	DEBUG_PRINT(INFO, L"    FrameBuffer: %p\n", BootConfig.GOPExports.FBAddress);
	DEBUG_PRINT(INFO, L"    Resolution: %u x %u\n",
		BootConfig.GOPExports.HorizontalResolution,
		BootConfig.GOPExports.VerticalResolution);
	DEBUG_PRINT(INFO, L"    PixelsPerScanLine: %u\n",
		BootConfig.GOPExports.PixelsPerScanLine);

	//
	// ------------------------------------------------------------
	// 11. Load font
	// ------------------------------------------------------------
	//

	DEBUG_PRINT(INFO, L"Loading font\n");
	DEBUG_PRINT(INFO, L"    Path: \\font.psf\n");

	void* FontFileBuffer = LoadFileIntoRAM(
		RootDir,
		L"font.psf",
		NULL
	);

	if (FontFileBuffer == NULL)
	{
		DEBUG_PRINT(INFO, L"Failed to load font\n");
		DEBUG_PRINT(INFO, L"    Path: \\font.psf\n");

		RootDir->Close(RootDir);

		return EFI_NOT_FOUND;
	}

	BootConfig.Font = (PSF1_HEADER*)FontFileBuffer;

	DEBUG_PRINT(INFO, L"Font loaded\n");
	DEBUG_PRINT(INFO, L"    Address: %p\n", FontFileBuffer);
	DEBUG_PRINT(INFO, L"    BootConfig.Font: %p\n",
		BootConfig.Font);

	//
	// ------------------------------------------------------------
	// 12. Load kernel
	// ------------------------------------------------------------
	//

	DEBUG_PRINT(INFO, L"Loading kernel\n");
	DEBUG_PRINT(INFO, L"    Path: \\KERNEL\n");

	UINTN KernelFileSize = 0;

	void* KernelRaw = LoadFileIntoRAM(
		RootDir,
		L"\\KERNEL",
		&KernelFileSize
	);

	if (KernelRaw == NULL)
	{
		DEBUG_PRINT(ERROR, L"Failed to load kernel\n");
		DEBUG_PRINT(ERROR, L"    Path: \\KERNEL\n");

		RootDir->Close(RootDir);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"Kernel loaded\n");
	DEBUG_PRINT(INFO, L"    Address: %p\n", KernelRaw);
	DEBUG_PRINT(INFO, L"    Size: %u bytes\n", KernelFileSize);

	//
	// ------------------------------------------------------------
	// 13. Validate DOS header
	// ------------------------------------------------------------
	//

	if (KernelFileSize < sizeof(EFI_IMAGE_DOS_HEADER))
	{
		DEBUG_PRINT(ERROR, L"Kernel is too small for a DOS header\n");
		DEBUG_PRINT(ERROR, L"    Kernel size: %u bytes\n", KernelFileSize);
		DEBUG_PRINT(ERROR, L"    Required: %u bytes\n",
			sizeof(EFI_IMAGE_DOS_HEADER));

		while (TRUE)
			__halt();
	}

	EFI_IMAGE_DOS_HEADER* DOSHeader =
		(EFI_IMAGE_DOS_HEADER*)KernelRaw;

	DEBUG_PRINT(INFO, L"Checking DOS header\n");
	DEBUG_PRINT(INFO, L"    e_magic: 0x%x\n", DOSHeader->e_magic);
	DEBUG_PRINT(INFO, L"    e_lfanew: %u\n", DOSHeader->e_lfanew);

	if (DOSHeader->e_magic != EFI_IMAGE_DOS_SIGNATURE)
	{
		DEBUG_PRINT(ERROR, L"Invalid DOS header signature\n");
		DEBUG_PRINT(ERROR, L"    Expected: 0x%x\n",
			EFI_IMAGE_DOS_SIGNATURE);
		DEBUG_PRINT(ERROR, L"    Received: 0x%x\n",
			DOSHeader->e_magic);

		while (TRUE)
			__halt();
	}

	//
	// ------------------------------------------------------------
	// 14. Validate NT header location
	// ------------------------------------------------------------
	//

	if (DOSHeader->e_lfanew < 0)
	{
		DEBUG_PRINT(ERROR, L"Invalid negative e_lfanew\n");
		DEBUG_PRINT(ERROR, L"    e_lfanew: %d\n", DOSHeader->e_lfanew);

		while (TRUE)
			__halt();
	}

	UINTN NTHeaderOffset =
		(UINTN)DOSHeader->e_lfanew;

	if (NTHeaderOffset >
		KernelFileSize - sizeof(EFI_IMAGE_NT_HEADERS64))
	{
		DEBUG_PRINT(ERROR, L"NT header exceeds kernel file bounds\n");
		DEBUG_PRINT(ERROR, L"    Kernel size: %u bytes\n", KernelFileSize);
		DEBUG_PRINT(ERROR, L"    NT header offset: %u\n", NTHeaderOffset);
		DEBUG_PRINT(ERROR, L"    NT header size: %u bytes\n",
			sizeof(EFI_IMAGE_NT_HEADERS64));

		while (TRUE)
			__halt();
	}

	EFI_IMAGE_NT_HEADERS64* NTHeaders =
		(EFI_IMAGE_NT_HEADERS64*)
		((UINT8*)KernelRaw + NTHeaderOffset);

	DEBUG_PRINT(INFO, L"NT header located\n");
	DEBUG_PRINT(INFO, L"    Address: %p\n", NTHeaders);
	DEBUG_PRINT(INFO, L"    Offset: %u\n", NTHeaderOffset);
	DEBUG_PRINT(INFO, L"    Signature: 0x%x\n", NTHeaders->Signature);

	//
	// ------------------------------------------------------------
	// 15. Validate NT signature
	// ------------------------------------------------------------
	//

	if (NTHeaders->Signature != EFI_IMAGE_NT_SIGNATURE)
	{
		DEBUG_PRINT(ERROR, L"Invalid NT header signature\n");
		DEBUG_PRINT(ERROR, L"    Expected: 0x%x\n",
			EFI_IMAGE_NT_SIGNATURE);
		DEBUG_PRINT(ERROR, L"    Received: 0x%x\n",
			NTHeaders->Signature);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"PE headers look valid\n");

	//
	// ------------------------------------------------------------
	// 16. Calculate kernel memory requirements
	// ------------------------------------------------------------
	//

	EFI_PHYSICAL_ADDRESS KernelBase =
		NTHeaders->OptionalHeader.ImageBase;

	UINTN KernelImageSize =
		NTHeaders->OptionalHeader.SizeOfImage;

	UINTN KernelPages =
		(KernelImageSize + 4095) / 4096;

	UINTN NumberOfSections =
		NTHeaders->FileHeader.NumberOfSections;

	UINTN EntryPointOffset =
		NTHeaders->OptionalHeader.AddressOfEntryPoint;

	DEBUG_PRINT(INFO, L"Kernel memory requirements\n");
	DEBUG_PRINT(INFO, L"    Preferred ImageBase: %p\n",
		(void*)KernelBase);
	DEBUG_PRINT(INFO, L"    SizeOfImage: %u bytes\n",
		KernelImageSize);
	DEBUG_PRINT(INFO, L"    Pages required: %u\n",
		KernelPages);
	DEBUG_PRINT(INFO, L"    Sections: %u\n",
		NumberOfSections);
	DEBUG_PRINT(INFO, L"    EntryPoint RVA: 0x%x\n",
		EntryPointOffset);

	//
	// ------------------------------------------------------------
	// 17. Allocate kernel at preferred address
	// ------------------------------------------------------------
	//

	Status = gBS->AllocatePages(
		AllocateAddress,
		EfiLoaderCode,
		KernelPages,
		&KernelBase
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(WARN, L"Preferred kernel address unavailable\n");
		DEBUG_PRINT(WARN, L"    Preferred address: %p\n",
			(void*)NTHeaders->OptionalHeader.ImageBase);
		DEBUG_PRINT(WARN, L"    Pages requested: %u\n", KernelPages);
		DEBUG_PRINT(WARN, L"    Status: %r\n", Status);

		//
		// Retry anywhere.
		//
		KernelBase =
			NTHeaders->OptionalHeader.ImageBase;

		Status = gBS->AllocatePages(
			AllocateAnyPages,
			EfiLoaderCode,
			KernelPages,
			&KernelBase
		);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(ERROR, L"Failed to allocate kernel memory\n");
			DEBUG_PRINT(ERROR, L"    Pages requested: %u\n", KernelPages);
			DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		DEBUG_PRINT(INFO, L"Kernel allocated at fallback address\n");
	}
	else
	{
		DEBUG_PRINT(INFO, L"Kernel allocated at preferred address\n");
	}

	DEBUG_PRINT(INFO, L"    KernelBase: %p\n", (void*)KernelBase);
	DEBUG_PRINT(INFO, L"    Pages: %u\n", KernelPages);

	//
	// ------------------------------------------------------------
	// 18. Copy PE sections into kernel memory
	// ------------------------------------------------------------
	//

	EFI_IMAGE_SECTION_HEADER* Section =
		(EFI_IMAGE_SECTION_HEADER*)
		((UINT8*)NTHeaders +
			sizeof(EFI_IMAGE_NT_HEADERS64));

	DEBUG_PRINT(INFO, L"Copying kernel sections\n");

	for (UINTN i = 0; i < NumberOfSections; i++)
	{
		DEBUG_PRINT(INFO, L"    Section %u\n", i);
		DEBUG_PRINT(INFO, L"        VirtualAddress: 0x%x\n",
			Section[i].VirtualAddress);
		DEBUG_PRINT(INFO, L"        RawOffset: 0x%x\n",
			Section[i].PointerToRawData);
		DEBUG_PRINT(INFO, L"        RawSize: %u bytes\n",
			Section[i].SizeOfRawData);

		gBS->CopyMem(
			(UINT8*)KernelBase +
			Section[i].VirtualAddress,

			(UINT8*)KernelRaw +
			Section[i].PointerToRawData,

			Section[i].SizeOfRawData
		);
	}

	DEBUG_PRINT(INFO, L"Kernel sections copied\n");

	//
	// ------------------------------------------------------------
	// 19. Calculate kernel entry point
	// ------------------------------------------------------------
	//

	typedef void (*KERNEL_ENTRY)(BOOT_CONFIG*);

	KERNEL_ENTRY KernelStart =
		(KERNEL_ENTRY)
		(KernelBase +
			NTHeaders->OptionalHeader.AddressOfEntryPoint);

	DEBUG_PRINT(INFO, L"Kernel entry point prepared\n");
	DEBUG_PRINT(INFO, L"    KernelBase: %p\n", (void*)KernelBase);
	DEBUG_PRINT(INFO, L"    EntryPoint RVA: 0x%x\n",
		NTHeaders->OptionalHeader.AddressOfEntryPoint);
	DEBUG_PRINT(INFO, L"    KernelStart: %p\n",
		(void*)KernelStart);

	//
	// ------------------------------------------------------------
	// 20. Get memory map size
	// ------------------------------------------------------------
	//

	UINTN MMapSize = 0;
	UINTN MMapKey = 0;
	UINTN DSize = 0;
	UINT32 DVer = 0;
	EFI_MEMORY_DESCRIPTOR* MMap = NULL;

	DEBUG_PRINT(INFO, L"Querying memory map size\n");

	Status = gBS->GetMemoryMap(
		&MMapSize,
		NULL,
		&MMapKey,
		&DSize,
		&DVer
	);

	//
	// EFI_BUFFER_TOO_SMALL is expected here.
	//
	if (Status != EFI_BUFFER_TOO_SMALL)
	{
		DEBUG_PRINT(ERROR, L"Initial GetMemoryMap() failed unexpectedly\n");
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);
		DEBUG_PRINT(ERROR, L"    Reported size: %u\n", MMapSize);
		DEBUG_PRINT(ERROR, L"    Descriptor size: %u\n", DSize);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"Memory map size obtained\n");
	DEBUG_PRINT(INFO, L"    Required size: %u bytes\n", MMapSize);
	DEBUG_PRINT(INFO, L"    Descriptor size: %u bytes\n", DSize);
	DEBUG_PRINT(INFO, L"    Descriptor version: 0x%x\n", DVer);

	//
	// ------------------------------------------------------------
	// 21. Add safety space and allocate memory map
	// ------------------------------------------------------------
	//

	UINTN MMapSafetyBuffer = 4096;

	MMapSize += MMapSafetyBuffer;

	DEBUG_PRINT(INFO, L"Allocating memory map buffer\n");
	DEBUG_PRINT(INFO, L"    Buffer size: %u bytes\n", MMapSize);
	DEBUG_PRINT(INFO, L"    Safety buffer: %u bytes\n",
		MMapSafetyBuffer);

	Status = gBS->AllocatePool(
		EfiLoaderData,
		MMapSize,
		(void**)&MMap
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to allocate memory map buffer\n");
		DEBUG_PRINT(ERROR, L"    Requested size: %u bytes\n", MMapSize);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"Memory map buffer allocated\n");
	DEBUG_PRINT(INFO, L"    Address: %p\n", MMap);
	DEBUG_PRINT(INFO, L"    Capacity: %u bytes\n", MMapSize);

	//
	// ------------------------------------------------------------
	// 22. Get actual memory map
	// ------------------------------------------------------------
	//

	Status = gBS->GetMemoryMap(
		&MMapSize,
		MMap,
		&MMapKey,
		&DSize,
		&DVer
	);

	if (EFI_ERROR(Status))
	{
		DEBUG_PRINT(ERROR, L"Failed to retrieve memory map\n");
		DEBUG_PRINT(ERROR, L"    Buffer: %p\n", MMap);
		DEBUG_PRINT(ERROR, L"    Buffer capacity: %u bytes\n", MMapSize);
		DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

		gBS->FreePool(MMap);

		while (TRUE)
			__halt();
	}

	DEBUG_PRINT(INFO, L"Memory map retrieved\n");
	DEBUG_PRINT(INFO, L"    Address: %p\n", MMap);
	DEBUG_PRINT(INFO, L"    Used size: %u bytes\n", MMapSize);
	DEBUG_PRINT(INFO, L"    Descriptor size: %u bytes\n", DSize);
	DEBUG_PRINT(INFO, L"    Descriptor version: 0x%x\n", DVer);
	DEBUG_PRINT(INFO, L"    Map key: %u\n", MMapKey);

	//
	// ------------------------------------------------------------
	// 23. Store memory map in BootConfig
	// ------------------------------------------------------------
	//

	BootConfig.MemMap = (void*)MMap;
	BootConfig.MemMapSize = (uint64_t)MMapSize;
	BootConfig.MemMapDescriptorSize = (uint64_t)DSize;

	DEBUG_PRINT(INFO, L"BootConfig memory map configured\n");
	DEBUG_PRINT(INFO, L"    MemMap: %p\n", BootConfig.MemMap);
	DEBUG_PRINT(INFO, L"    MemMapSize: %llu\n",
		BootConfig.MemMapSize);
	DEBUG_PRINT(INFO, L"    DescriptorSize: %llu\n",
		BootConfig.MemMapDescriptorSize);

	//
// ------------------------------------------------------------
// 24. Exit UEFI Boot Services
// ------------------------------------------------------------
//

	DEBUG_PRINT(INFO, L"Preparing to exit Boot Services...\n");

	//
	// IMPORTANT:
	// From the final GetMemoryMap() until ExitBootServices(),
	// avoid Boot Services calls or anything that could change
	// the memory map.
	//
	Status = gBS->ExitBootServices(
		ImageHandle,
		MMapKey
	);

	if (EFI_ERROR(Status))
	{
		//
		// ExitBootServices() failed.
		//
		// The most important case is EFI_INVALID_PARAMETER,
		// which normally means the memory-map key became stale.
		//

		DEBUG_PRINT(WARN, L"First ExitBootServices() failed\n");
		DEBUG_PRINT(WARN, L"    Status: %r\n", Status);

		//
		// Refresh the memory map.
		//
		UINTN NewMMapSize = 0;

		Status = gBS->GetMemoryMap(
			&NewMMapSize,
			NULL,
			&MMapKey,
			&DSize,
			&DVer
		);

		if (Status != EFI_BUFFER_TOO_SMALL)
		{
			DEBUG_PRINT(ERROR, L"Failed to query refreshed memory map size\n");
			DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		//
		// Give the new map some extra room.
		//
		MMapSize = NewMMapSize + 4096;

		DEBUG_PRINT(INFO, L"Allocating refreshed memory map buffer\n");
		DEBUG_PRINT(INFO, L"    Required size: %u bytes\n", NewMMapSize);
		DEBUG_PRINT(INFO, L"    Buffer size: %u bytes\n", MMapSize);

		//
		// We no longer need the old map buffer.
		//
		Status = gBS->FreePool(MMap);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(ERROR, L"Failed to free old memory map buffer\n");
			DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		//
		// Allocate the new buffer.
		//
		Status = gBS->AllocatePool(
			EfiLoaderData,
			MMapSize,
			(void**)&MMap
		);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(ERROR, L"Failed to allocate refreshed memory map buffer\n");
			DEBUG_PRINT(ERROR, L"    Requested size: %u bytes\n", MMapSize);
			DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		//
		// Get the current memory map.
		//
		Status = gBS->GetMemoryMap(
			&MMapSize,
			MMap,
			&MMapKey,
			&DSize,
			&DVer
		);

		if (EFI_ERROR(Status))
		{
			DEBUG_PRINT(ERROR, L"Failed to retrieve refreshed memory map\n");
			DEBUG_PRINT(ERROR, L"    Buffer: %p\n", MMap);
			DEBUG_PRINT(ERROR, L"    Buffer capacity: %u bytes\n", MMapSize);
			DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}

		//
		// Update BootConfig.
		//
		BootConfig.MemMap = (void*)MMap;
		BootConfig.MemMapSize = (uint64_t)MMapSize;
		BootConfig.MemMapDescriptorSize = (uint64_t)DSize;

		//
		// IMPORTANT:
		// Do not call DEBUG_PRINT() here.
		//
		// GetMemoryMap() gave us the current MapKey.
		// ExitBootServices() must happen immediately.
		//
		Status = gBS->ExitBootServices(
			ImageHandle,
			MMapKey
		);

		if (EFI_ERROR(Status))
		{
			//
			// If this happens, the map changed again.
			// We will deal with repeated retries properly later.
			//
			DEBUG_PRINT(ERROR, L"ExitBootServices() retry failed\n");
			DEBUG_PRINT(ERROR, L"    Status: %r\n", Status);

			while (TRUE)
				__halt();
		}
	}

	//
	// Boot Services are now gone.
	// DO NOT use gBS from this point onward.
	//

	KernelStart(&BootConfig);

	while (TRUE)
		__halt();

	//
	// ------------------------------------------------------------
	// 25. Jump to kernel
	// ------------------------------------------------------------
	//

	KernelStart(&BootConfig);

	//
	// KernelStart should never return.
	//
	while (TRUE)
		__halt();

	return EFI_SUCCESS;
}

EFI_STATUS EFIAPI UefiUnload()
{
	return EFI_SUCCESS;
}