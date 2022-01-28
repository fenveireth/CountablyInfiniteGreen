#define _UNICODE

#include <windows.h>
#include <stdbool.h>
#include <stdio.h>

#define EMBD_FILE(v) extern const char _binary_##v##_start[]; extern const char _binary_##v##_end[]

EMBD_FILE(libloader);
EMBD_FILE(FenLoader);
EMBD_FILE(0Harmony);
EMBD_FILE(Mono_Cecil);
EMBD_FILE(MonoMod_RuntimeDetour);
EMBD_FILE(MonoMod_Utils);

#undef EMBD_FILE

static bool extract_files()
{
	int size;
	int w;
	FILE* file;
#define DMP(v, fn) \
	size = _binary_##v##_end - _binary_##v##_start; \
	file = fopen(fn, "wb"); \
	if (!file) { \
		return false; \
	} \
	w = fwrite(_binary_##v##_start, 1, size, file); \
	fclose(file); \
	if (w != size) \
		return false;

	DMP(libloader, "libloader.dll");
	DMP(FenLoader, "FenLoader.dll");
	DMP(0Harmony, "0Harmony.dll");
	DMP(Mono_Cecil, "Mono.Cecil.dll");
	DMP(MonoMod_RuntimeDetour, "MonoMod.RuntimeDetour.dll");
	DMP(MonoMod_Utils, "MonoMod.Utils.dll");
#undef DMP

	return true;
}

int WINAPI WinMain(HINSTANCE inst, HINSTANCE prev, PSTR _cmd_line, int show)
{
	UNREFERENCED_PARAMETER(inst);
	UNREFERENCED_PARAMETER(prev);
	UNREFERENCED_PARAMETER(_cmd_line);
	UNREFERENCED_PARAMETER(show);

	// Pass arguments on new command line
	wchar_t* cmd_line = GetCommandLineW();
	int argv0end = 0;
	bool quot = false;
	if (cmd_line[0] == L'"') {
		quot = true;
		++argv0end;
	}
	for (wchar_t c; (c = cmd_line[argv0end]) != 0; ++argv0end) {
		if (c == L'\\') {
			++argv0end;
			continue;
		}
		if (c == L' ' && !quot)
			break;
		if (c == L'"')
			quot = false;
	}

	wchar_t new_cmd[1024] = L"\"Golden Treasure - The Great Green.exe\"";
	wcsncpy(new_cmd + 39, cmd_line + argv0end, 1024 - 39 - 1);

	// start in suspended mode
	STARTUPINFO si = { 0 };
	si.cb = sizeof(si);
	PROCESS_INFORMATION proc_info = { 0 };
	if (!CreateProcessW(NULL, new_cmd, NULL, NULL, FALSE, CREATE_SUSPENDED,
			NULL, NULL, &si, &proc_info)) {
		MessageBox(NULL, L"Game could not be started from current directory\n\n"
			L"Please run this launcher from the game root directory",
			NULL, MB_ICONERROR);
		return 1;
	}

	if (!extract_files())
	{
		TerminateProcess(proc_info.hProcess, 0);
		MessageBox(NULL, L"Cannot write to game directory\n\n"
			L"Make sure the game is not already running", NULL, MB_ICONERROR);
		goto end;
	}

	// inject doorstop :
	// Loaded at same address for all process
	HMODULE kernel32 = GetModuleHandle(L"Kernel32");
	LPTHREAD_START_ROUTINE load_library =
		(LPTHREAD_START_ROUTINE)GetProcAddress(kernel32, "LoadLibraryW");
	void* llarg = VirtualAllocEx(proc_info.hProcess, NULL, 512, MEM_COMMIT, PAGE_READWRITE);
	WriteProcessMemory(proc_info.hProcess, llarg, L"libloader.dll", 28, NULL);
	HANDLE rthread = CreateRemoteThread(proc_info.hProcess, NULL, 0,
		load_library, llarg, 0, NULL);
	WaitForSingleObject(rthread, INFINITE);
	DWORD res = 0;
	GetExitCodeThread(rthread, &res);
	CloseHandle(rthread);
	VirtualFreeEx(proc_info.hProcess, llarg, 512, MEM_RELEASE);

end:
	ResumeThread(proc_info.hThread);
	WaitForSingleObject(proc_info.hThread, INFINITE);
	CloseHandle(proc_info.hThread);
	CloseHandle(proc_info.hProcess);
	return 0;
}
