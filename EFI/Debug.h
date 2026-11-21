#pragma once

#define DEBUG_PRINT(Level, Format, ...) \
    Print(L"[" Level L"] " PROJ L"/" FILE L"/%a()/%d> " Format L"\n", \
        __FUNCTION__, __LINE__, ##__VA_ARGS__)