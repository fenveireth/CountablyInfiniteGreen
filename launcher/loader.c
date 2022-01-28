#define _UNICODE

#include <windows.h>

// Adapted from Unity Doorstop v3.4
// https://github.com/NeighTools/UnityDoorstop

#define ASSERT_SOFT(test, ...)               \
	if(!(test))                                \
	{                                          \
		return __VA_ARGS__;                      \
	}

static HANDLE h_heap;

static void* _malloc(size_t size)
{
	return HeapAlloc(h_heap, HEAP_GENERATE_EXCEPTIONS, size);
}

static void _free(void *mem)
{
	HeapFree(h_heap, 0, mem);
}

// PE format uses RVAs (Relative Virtual Addresses) to save addresses relative to the base of the module
// More info: https://en.wikibooks.org/wiki/X86_Disassembly/Windows_Executable_Files#Relative_Virtual_Addressing_(RVA)
//
// This helper macro converts the saved RVA to a fully valid pointer to the data in the PE file
#define RVA2PTR(t,base,rva) ((t)(((PCHAR) (base)) + (rva)))

/**
 * \brief Hooks the given function through the Import Address Table
 *        This is a simplified version that doesn't does lookup directly in the initialized IAT.
 *        This is usable to hook system DLLs like kernel32.dll assuming the process wasn't already hooked.
 * \param dll Module to hook
 * \param target_function Address of the target function to hook
 * \param detour_function Address of the detour function
 * \return TRUE if successful, otherwise FALSE
 */
static BOOL iat_hook(HMODULE dll, char const *target_dll, void *target_function, void *detour_function)
{
	IMAGE_DOS_HEADER *mz = (PIMAGE_DOS_HEADER)dll;
	IMAGE_NT_HEADERS *nt = RVA2PTR(PIMAGE_NT_HEADERS, mz, mz->e_lfanew);
	IMAGE_IMPORT_DESCRIPTOR *imports = RVA2PTR(IMAGE_IMPORT_DESCRIPTOR*, mz, nt->OptionalHeader.DataDirectory[
	                                                 IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);

	for (int i = 0; imports[i].Characteristics; i++) {
		char *name = RVA2PTR(char*, mz, imports[i].Name);

		if (lstrcmpiA(name, target_dll) != 0)
			continue;

		void **thunk = RVA2PTR(void**, mz, imports[i].FirstThunk);

		for (; *thunk; thunk++) {
			void *import = *thunk;

			if (import != target_function)
				continue;

			DWORD old_state;
			if (!VirtualProtect(thunk, sizeof(void*), PAGE_READWRITE, &old_state))
				return FALSE;

			*thunk = (void*)detour_function;

			VirtualProtect(thunk, sizeof(void*), old_state, &old_state);

			return TRUE;
		}
	}

	return FALSE;
}

static size_t get_module_path(HMODULE module, wchar_t **result, size_t *size, size_t free_space)
{
    DWORD i = 0;
    DWORD len, s;
    *result = NULL;
    do {
        if (*result != NULL)
            _free(*result);
        i++;
        s = i * MAX_PATH + 1;
        *result = _malloc(sizeof(wchar_t) * s);
        len = GetModuleFileNameW(module, *result, s);
    }
    while (GetLastError() == ERROR_INSUFFICIENT_BUFFER || s - len < free_space);

    if (size != NULL)
        *size = s;
    return len;
}

// Here we define the pointers to some functions within mono.dll
// Note to C learners: these are not signature definitions, but rather "variable"
// definitions with the function pointer type.

// Note: we use void* instead of the real intented structs defined in mono API
// This way we don't need to include or define any of Mono's structs, which saves space
// This, obviously, comes with a drawback of not being able to easily access the contents of the structs

void * (*mono_thread_current)();
void (*mono_thread_set_main)(void *);

void *(*mono_jit_init_version)(const char *root_domain_name, const char *runtime_version);
void *(*mono_domain_assembly_open)(void *domain, const char *name);
void *(*mono_assembly_get_image)(void *assembly);
void *(*mono_assembly_load_from)(void *image, const char* name, void* status);
void *(*mono_runtime_invoke)(void *method, void *obj, void **params, void **exc);

void *(*mono_method_desc_new)(const char *name, int include_namespace);
void* (*mono_method_desc_search_in_image)(void* desc, void* image);
void *(*mono_method_desc_search_in_class)(void *desc, void *klass);
void (*mono_method_desc_free)(void *desc);
void *(*mono_method_signature)(void *method);
UINT32 (*mono_signature_get_param_count)(void *sig);

void (*mono_domain_set_config)(void *domain, char *base_dir, char *config_file_name);
void *(*mono_array_new)(void *domain, void *eclass, uintptr_t n);
void *(*mono_get_string_class)();

char *(*mono_assembly_getrootdir)();

// Additional funcs to bootstrap custom MONO
void (*mono_set_dirs)(const char* assembly_dir, const char* config_dir);
void (*mono_config_parse)(const char* filename);
void (*mono_set_assemblies_path)(const char* path);
void *(*mono_object_to_string)(void* obj, void** exc);
char *(*mono_string_to_utf8)(void* s);

void *(*mono_image_open_from_data)(void *data, DWORD data_len, int need_copy, void *status);
void *(*mono_image_open_from_data_with_name)(void *data, DWORD data_len, int need_copy, void *status, int refonly,
                                             const char *name);

void* (*mono_get_exception_class)();
void* (*mono_object_get_virtual_method)(void* obj_raw, void* method);

void* (*mono_jit_parse_options)(int argc, const char** argv);

typedef enum {
    MONO_DEBUG_FORMAT_NONE,
    MONO_DEBUG_FORMAT_MONO,
    /* Deprecated, the mdb debugger is not longer supported. */
    MONO_DEBUG_FORMAT_DEBUGGER
} MonoDebugFormat;

void* (*mono_debug_init)(MonoDebugFormat format);
void* (*mono_debug_domain_create)(void* domain);

/**
* \brief Loads Mono C API function pointers so that the above definitions can be called.
* \param mono_lib Mono.dll module.
*/
static void load_mono_functions(HMODULE mono_lib) {
    // Enjoy the fact that C allows such sloppy casting
    // In C++ you would have to cast to the precise function pointer type
#define GET_MONO_PROC(name) name = (void*)GetProcAddress(mono_lib, #name)

    // Find and assign all our functions that we are going to use
    GET_MONO_PROC(mono_domain_assembly_open);
    GET_MONO_PROC(mono_assembly_get_image);
    GET_MONO_PROC(mono_assembly_load_from);
    GET_MONO_PROC(mono_runtime_invoke);
    GET_MONO_PROC(mono_jit_init_version);
    GET_MONO_PROC(mono_method_desc_new);
    GET_MONO_PROC(mono_method_desc_search_in_class);
    GET_MONO_PROC(mono_method_desc_search_in_image);
    GET_MONO_PROC(mono_method_desc_free);
    GET_MONO_PROC(mono_method_signature);
    GET_MONO_PROC(mono_signature_get_param_count);
    GET_MONO_PROC(mono_array_new);
    GET_MONO_PROC(mono_get_string_class);
    GET_MONO_PROC(mono_assembly_getrootdir);
    GET_MONO_PROC(mono_thread_current);
    GET_MONO_PROC(mono_thread_set_main);
    GET_MONO_PROC(mono_domain_set_config);
    GET_MONO_PROC(mono_set_dirs);
    GET_MONO_PROC(mono_config_parse);
    GET_MONO_PROC(mono_set_assemblies_path);
    GET_MONO_PROC(mono_object_to_string);
    GET_MONO_PROC(mono_string_to_utf8);
    GET_MONO_PROC(mono_image_open_from_data);
    GET_MONO_PROC(mono_image_open_from_data_with_name);
    GET_MONO_PROC(mono_get_exception_class);
    GET_MONO_PROC(mono_object_get_virtual_method);
    GET_MONO_PROC(mono_jit_parse_options);
    GET_MONO_PROC(mono_debug_init);
    GET_MONO_PROC(mono_debug_domain_create);

#undef GET_MONO_PROC
}

// The hook for mono_jit_init_version
// We use this since it will always be called once to initialize Mono's JIT
void doorstop_invoke(void *domain)
{
	const wchar_t* target_assembly = L"FenLoader.dll";
	mono_thread_set_main(mono_thread_current());

	const int len = WideCharToMultiByte(CP_UTF8, 0, target_assembly, -1, NULL, 0, NULL, NULL);
	char *dll_path = _malloc(sizeof(char) * len);
	WideCharToMultiByte(CP_UTF8, 0, target_assembly, -1, dll_path, len, NULL, NULL);

	wchar_t *app_path = NULL;
	get_module_path(NULL, &app_path, NULL, 0);

	// Load our custom assembly into the domain
	void *assembly = mono_domain_assembly_open(domain, dll_path);
	_free(dll_path);
	ASSERT_SOFT(assembly != NULL);

	// Get assembly's image that contains CIL code
	void *image = mono_assembly_get_image(assembly);
	ASSERT_SOFT(image != NULL);

	// Create a descriptor for a random Main method
	void *desc = mono_method_desc_new("*:Main", FALSE);

	// Find the first possible Main method in the assembly
	void *method = mono_method_desc_search_in_image(desc, image);
	ASSERT_SOFT(method != NULL);

	void *signature = mono_method_signature(method);

	// Get the number of parameters in the signature
	UINT32 params = mono_signature_get_param_count(signature);

	void **args = NULL;
	if (params == 1) {
		// If there is a parameter, it's most likely a string[].
		void *args_array = mono_array_new(domain, mono_get_string_class(), 0);
		args = _malloc(sizeof(void*) * 1);
		args[0] = args_array;
	}

	// Note: we use the runtime_invoke route since jit_exec will not work on DLLs
	void *exc = NULL;
	mono_runtime_invoke(method, NULL, args, &exc);

	// cleanup method_desc
	mono_method_desc_free(desc);

	if (args != NULL) {
		_free(app_path);
		_free(args);
		args = NULL;
	}
}

static void *init_doorstop_mono(const char *root_domain_name, const char *runtime_version)
{
	void *domain = mono_jit_init_version(root_domain_name, runtime_version);
	doorstop_invoke(domain);
	return domain;
}

static BOOL initialized = FALSE;

static void* WINAPI get_proc_address_detour(HMODULE module, char const *name)
{
#define REDIRECT_INIT(init_name, init_func, target)           \
	if (lstrcmpA(name, init_name) == 0) {                       \
		if (!initialized) {                                       \
			initialized = TRUE;                                     \
			init_func(module);                                      \
		}                                                         \
		return (void*)(target);                                   \
	}
	REDIRECT_INIT("mono_jit_init_version", load_mono_functions, init_doorstop_mono);
	return (void*)GetProcAddress(module, name);

#undef REDIRECT_INIT
}

static HANDLE stdout_handle;

BOOL WINAPI close_handle_hook(HANDLE handle)
{
	if (stdout_handle && handle == stdout_handle)
		return TRUE;
	return CloseHandle(handle);
}

static void __attribute__((constructor)) on_load()
{
	h_heap = GetProcessHeap();
	stdout_handle = GetStdHandle(STD_OUTPUT_HANDLE);
	HMODULE unity_dll = GetModuleHandle(L"UnityPlayer");
	iat_hook(unity_dll, "kernel32.dll", &GetProcAddress, &get_proc_address_detour);
	iat_hook(unity_dll, "kernel32.dll", &CloseHandle, &close_handle_hook);
}
