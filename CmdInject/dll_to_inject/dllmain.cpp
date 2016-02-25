// dllmain.cpp : Defines the entry point for the DLL application.
#include "stdafx.h"
#include <process.h>
#include <vector>

#include <iostream>
#include <string>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <queue>
#include <map>


//Almost all of this is from clink

/*
BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
					 )
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}
*/


static void LOG_ERROR(const char* str) {
	MessageBoxA(NULL, str, NULL, NULL);
	throw str;
}

typedef enum {
	SEARCH_IAT_BY_ADDR = 0,
	SEARCH_IAT_BY_NAME = 1,
} search_iat_type_e;

typedef enum {
	HOOK_TYPE_IAT_BY_ADDR = SEARCH_IAT_BY_ADDR,
	HOOK_TYPE_IAT_BY_NAME = SEARCH_IAT_BY_NAME,
	HOOK_TYPE_JMP,
} hook_type_e;

typedef struct {
	hook_type_e     type;
	void*           base;           // unused by jmp-type
	const char*     dll;            // null makes iat-types search all
	void*           name_or_addr;   // name only for jmp-type
	void*           hook;
} hook_decl_t;

static void* rva_to_addr(void* base, unsigned rva)
{
	return (char*)(uintptr_t)rva + (uintptr_t)base;
}

IMAGE_NT_HEADERS* get_nt_headers(void* base)
{
	IMAGE_DOS_HEADER* dos_header;

	dos_header = (IMAGE_DOS_HEADER*)base;
	return (IMAGE_NT_HEADERS*)((char*)base + dos_header->e_lfanew);
}

IMAGE_IMPORT_DESCRIPTOR* get_data_directory(void* base, int index, int* size)
{
	IMAGE_NT_HEADERS* nt_headers;
	IMAGE_DATA_DIRECTORY* data_dir;

	nt_headers = get_nt_headers(base);
	data_dir = nt_headers->OptionalHeader.DataDirectory + index;
	if (data_dir == NULL)
	{
		return NULL;
	}

	if (data_dir->VirtualAddress == 0)
	{
		return NULL;
	}

	if (size != NULL)
	{
		*size = data_dir->Size;
	}

	return (IMAGE_IMPORT_DESCRIPTOR*)rva_to_addr(base, data_dir->VirtualAddress);
}

static void** iterate_imports(
	void* base,
	const char* dll,
	const void* param,
	void** (*callback)(void*, IMAGE_IMPORT_DESCRIPTOR*, const void*)
	)
{
	IMAGE_IMPORT_DESCRIPTOR* iid;

	iid = get_data_directory(base, 1, NULL);
	if (iid == NULL)
	{
		LOG_ERROR("Failed to find import desc for base");
		return 0;
	}

	while (iid->Characteristics)
	{
		char* name;
		size_t len;

		len = (dll != NULL) ? strlen(dll) : 0;
		name = (char*)rva_to_addr(base, iid->Name);
		if (dll == NULL || _strnicmp(name, dll, len) == 0)
		{
			void** ret;

			ret = callback(base, iid, param);
			if (ret != NULL)
			{
				return ret;
			}
		}

		++iid;
	}

	return NULL;
}

static void** import_by_name(
	void* base,
	IMAGE_IMPORT_DESCRIPTOR* iid,
	const void* func_name
	)
{
	void** at = (void**)rva_to_addr(base, iid->FirstThunk);
	intptr_t* nt = (intptr_t*)rva_to_addr(base, iid->OriginalFirstThunk);
	while (*at != 0 && *nt != 0)
	{
		// Check that this import is imported by name (MSB not set)
		if (*nt > 0)
		{
			unsigned rva = (unsigned)(*nt & 0x7fffffff);
			IMAGE_IMPORT_BY_NAME* iin = (IMAGE_IMPORT_BY_NAME*)rva_to_addr(base, rva);

			if (_stricmp(iin->Name, (char *)func_name) == 0)
				return at;
		}

		++at;
		++nt;
	}

	return NULL;
}

static void** import_by_addr(
	void* base,
	IMAGE_IMPORT_DESCRIPTOR* iid,
	const void* func_addr
	)
{
	void** at = (void**)rva_to_addr(base, iid->FirstThunk);
	while (*at != 0)
	{
		uintptr_t addr = (uintptr_t)(*at);
		void* addr_loc = at;

		if (addr == (uintptr_t)func_addr)
		{
			return at;
		}

		++at;
	}

	return NULL;
}

void* get_export(void* base, const char* func_name)
{
	IMAGE_NT_HEADERS* nt_header;
	IMAGE_DATA_DIRECTORY* data_dir;
	int i;

	IMAGE_DOS_HEADER* dos_header = (IMAGE_DOS_HEADER*)base;
	nt_header = (IMAGE_NT_HEADERS*)((char*)base + dos_header->e_lfanew);
	data_dir = nt_header->OptionalHeader.DataDirectory;

	if (data_dir == NULL)
	{
		LOG_ERROR("Failed to find export table for base");// %p", base);
		return NULL;
	}

	if (data_dir->VirtualAddress == 0)
	{
		LOG_ERROR("No export directory found at base %p");//", base);
		return NULL;
	}

	IMAGE_EXPORT_DIRECTORY* ied = (IMAGE_EXPORT_DIRECTORY*)rva_to_addr(base, data_dir->VirtualAddress);
	DWORD* names = (DWORD*)rva_to_addr(base, ied->AddressOfNames);
	WORD* ordinals = (WORD*)rva_to_addr(base, ied->AddressOfNameOrdinals);
	DWORD* addresses = (DWORD*)rva_to_addr(base, ied->AddressOfFunctions);

	for (i = 0; i < (int)(ied->NumberOfNames); ++i)
	{
		const char* export_name = (const char*)rva_to_addr(base, names[i]);
		if (_stricmp(export_name, func_name))
		{
			continue;
		}

		WORD ordinal = ordinals[i];
		return rva_to_addr(base, addresses[ordinal]);
	}

	return NULL;
}

void** get_import_by_name(void* base, const char* dll, const char* func_name)
{
	return iterate_imports(base, dll, func_name, import_by_name);
}

void** get_import_by_addr(void* base, const char* dll, void* func_addr)
{
	return iterate_imports(base, dll, func_addr, import_by_addr);
}

static void* get_proc_addr(const char* dll, const char* func_name)
{
	void* base;

	base = LoadLibraryA(dll);
	if (base == NULL)
	{
		LOG_ERROR("Failed to load library '%s')");// ", dll);
		return NULL;
	}

	return get_export(base, func_name);
}

struct region_info_t
{
	void*       base;
	size_t      size;
	unsigned    protect;
};

void get_region_info(void* addr, struct region_info_t* region_info)
{
	MEMORY_BASIC_INFORMATION mbi;
	VirtualQuery(addr, &mbi, sizeof(mbi));

	region_info->base = mbi.BaseAddress;
	region_info->size = mbi.RegionSize;
	region_info->protect = mbi.Protect;
}

//------------------------------------------------------------------------------
void set_region_write_state(struct region_info_t* region_info, int state)
{
	DWORD unused;
	VirtualProtect(
		region_info->base,
		region_info->size,
		(state ? PAGE_EXECUTE_READWRITE : region_info->protect),
		&unused
		);
}

int write_vm(void* proc_handle, void* dest, const void* src, size_t size)
{
	BOOL ok;
	ok = WriteProcessMemory((HANDLE)proc_handle, dest, src, size, NULL);
	return (ok != FALSE);
}

static void* current_proc()
{
	return (void*)GetCurrentProcess();
}

static void write_addr(void** where, void* to_write)
{
	struct region_info_t region_info;

	get_region_info(where, &region_info);
	set_region_write_state(&region_info, 1);

	if (!write_vm(current_proc(), where, &to_write, sizeof(to_write)))
	{
		LOG_ERROR("VM write to %p failed");// (err = %d)", where, GetLastError());
	}

	set_region_write_state(&region_info, 0);
}

void* hook_iat(
	void* base,
	const char* dll,
	const char* func_name,
	void* hook,
	int find_by_name
	)
{
	void* func_addr;
	void* prev_addr;
	void** imp;

	// Find entry and replace it.
	if (find_by_name)
	{
		imp = get_import_by_name(base, NULL, func_name);
	}
	else
	{
		// Get the address of the function we're going to hook.
		func_addr = get_proc_addr(dll, func_name);
		if (func_addr == NULL)
		{
			LOG_ERROR("Failed to find function");// '%s' in '%s'", func_name, dll);
			return NULL;
		}

		imp = get_import_by_addr(base, NULL, func_addr);
	}

	if (imp == NULL)
	{
		LOG_ERROR("Unable to find import in IAT");// (by_name = %d)", find_by_name);
		return NULL;
	}

	prev_addr = *imp;
	write_addr(imp, hook);

	FlushInstructionCache(current_proc(), 0, 0);
	return prev_addr;
}

static void* apply_hook_iat_inner(void* self, const hook_decl_t* hook, int by_name)
{
	void* addr = hook_iat(hook->base, hook->dll, (const char*)hook->name_or_addr, hook->hook, by_name);
	if (addr == NULL)
	{
		LOG_ERROR("Unable to hook in IAT at base");
			//hook->name_or_addr,
			//hook->base
		return 0;
	}

	// If the target's IAT was hooked then the hook destination is now
	// stored in 'addr'. We hook ourselves with this address to maintain
	// any IAT hooks that may already exist.
	if (hook_iat(self, NULL, (const char*)hook->name_or_addr, addr, 1) == 0)
	{
		LOG_ERROR("Failed to hook own IAT");// %s", hook->name_or_addr);
		return 0;
	}

	return addr;
}

void* get_alloc_base(void* addr)
{
	MEMORY_BASIC_INFORMATION mbi;
	VirtualQuery(addr, &mbi, sizeof(mbi));
	return mbi.AllocationBase;
}

void* apply_hook_iat(hook_decl_t hook) {
	void* self = get_alloc_base(apply_hook_iat);

	return apply_hook_iat_inner(self, &hook, hook.type);
}




static void* follow_jump(void* addr)
{
	void* dest;
	char* t = (char*)addr;
	int* imm = (int*)(t + 2);

	if (*((unsigned short*)addr) != 0x25ff)
		return addr;

#ifdef _M_X64
	dest = t + *imm + 6;
#elif defined _M_IX86
	dest = (void*)(intptr_t)(*imm);
#endif

	return dest;
}

#define sizeof_array(x) (sizeof((x)) / sizeof((x)[0]))

static int get_mask_size(unsigned mask)
{
	// Just for laughs, a sledgehammer for a nut.
	mask &= 0x01010101;
	mask += mask >> 16;
	mask += mask >> 8;
	return mask & 0x0f;
}

static int get_instruction_length(void* addr)
{
	unsigned prolog;
	int i;

	struct asm_tag_t
	{
		unsigned expected;
		unsigned mask;
	};

	struct asm_tag_t asm_tags[] = {
#ifdef _M_X64
	{ 0x38ec8348, 0xffffffff },  // sub rsp,38h  
	{ 0x0000f3ff, 0x0000ffff },  // push rbx  
	{ 0x00005340, 0x0000ffff },  // push rbx
	{ 0x00dc8b4c, 0x00ffffff },  // mov r11, rsp
	{ 0x0000b848, 0x0000f8ff },  // mov reg64, imm64  = 10-byte length
#elif defined _M_IX86
	{ 0x0000ff8b, 0x0000ffff },  // mov edi,edi  
#endif
	{ 0x000000e9, 0x000000ff },  // jmp addr32        = 5-byte length
	};

	prolog = *(unsigned*)(addr);
	for (i = 0; i < sizeof_array(asm_tags); ++i)
	{
		int length;
		unsigned expected = asm_tags[i].expected;
		unsigned mask = asm_tags[i].mask;

		if (expected != (prolog & mask))
		{
			continue;
		}

		length = get_mask_size(mask);

		// Checks for instructions that "expected" only partially matches.
		if (expected == 0x0000b848)
		{
			length = 10;
		}
		else if (expected == 0xe9)
		{
			// jmp [imm32]
			length = 5;
		}

		return length;
	}

	return 0;
}

static char* alloc_trampoline(void* hint)
{
	static const int size = 0x100;
	void* trampoline;
	void* vm_alloc_base;
	char* tramp_page;
	SYSTEM_INFO sys_info;

	GetSystemInfo(&sys_info);

	do
	{
		vm_alloc_base = get_alloc_base(hint);
		vm_alloc_base = vm_alloc_base ? vm_alloc_base : hint;
		tramp_page = (char*)vm_alloc_base - sys_info.dwPageSize;

		trampoline = VirtualAlloc(
			tramp_page,
			sys_info.dwPageSize,
			MEM_COMMIT | MEM_RESERVE,
			PAGE_EXECUTE_READWRITE
			);

		hint = tramp_page;
	} while (trampoline == NULL);

	return (char*)trampoline;
}

static char* write_rel_jmp(char* write, void* dest)
{
	intptr_t disp;
	struct {
		char a;
		char b[4];
	} buffer;

	// jmp <displacement>
	disp = (intptr_t)dest;
	disp -= (intptr_t)write;
	disp -= 5;

	buffer.a = 0xe9;
	*(int*)buffer.b = (int)disp;

	if (!write_vm(current_proc(), write, &buffer, sizeof(buffer)))
	{
		LOG_ERROR("VM write to %p failed (err = %d)");// , write, GetLastError());
		return NULL;
	}

	return write + 5;
}

static char* write_trampoline_in(char* write, void* to_hook, int n)
{
	int i;

	// Copy
	for (i = 0; i < n; ++i)
	{
		if (!write_vm(current_proc(), write, (char*)to_hook + i, 1))
		{
			LOG_ERROR("VM write to %p failed (err = %d)");// , write, GetLastError());
			return NULL;
		}
		++write;
	}

	// If the moved instruction is JMP (e9) then the displacement is relative
	// to its original location. As we have relocated the jump the displacement
	// needs adjusting.
	if (*(unsigned char*)to_hook == 0xe9)
	{
		int displacement = *(int*)(write - 4);
		intptr_t old_ip = (intptr_t)to_hook + n;
		intptr_t new_ip = (intptr_t)write;

		*(int*)(write - 4) = (int)(displacement + old_ip - new_ip);
	}

	return (char*)write_rel_jmp(write, (char*)to_hook + n);
}

static char* write_trampoline_out(char* write, void* to_hook, void* hook)
{
	struct {
		char a[2];
		char b[4];
		char c[sizeof(void*)];
	} inst;
	short temp;
	unsigned rel_addr;
	int i;
	char* patch;

	rel_addr = 0;
	patch = (char*)to_hook - 5;

	// Check we've got a nop slide or int3 block to patch into.
	for (i = 0; i < 5; ++i)
	{
		unsigned char c = patch[i];
		if (c != 0x90 && c != 0xcc)
		{
			LOG_ERROR("No nop slide or int3 block detected prior to hook target.");
			return NULL;
		}
	}

	// Patch the API.
	patch = write_rel_jmp(patch, write);
	temp = 0xf9eb;
	if (!write_vm(current_proc(), patch, &temp, sizeof(temp)))
	{
		LOG_ERROR("VM write to %p failed (err = %d)");// , patch, GetLastError());
		return NULL;
	}

	// Long jmp.
	*(short*)inst.a = 0x25ff;

#ifdef _M_IX86
	rel_addr = (intptr_t)write + 6;
#endif

	*(int*)inst.b = rel_addr;
	*(void**)inst.c = hook;

	if (!write_vm(current_proc(), write, &inst, sizeof(inst)))
	{
		LOG_ERROR("VM write to %p failed (err = %d)");//, write, GetLastError());
		return NULL;
	}

	return write + sizeof(inst);
}

static void* hook_jmp_impl(void* to_hook, void* hook)
{
	struct region_info_t region_info;
	char* trampoline;
	char* write;
	int inst_len;

	to_hook = follow_jump(to_hook);

	// Work out the length of the first instruction. It will be copied it into
	// the trampoline.
	inst_len = get_instruction_length(to_hook);
	if (inst_len <= 0)
	{
		LOG_ERROR("Unable to match instruction %08X");// , *(int*)(to_hook));
		return NULL;
	}

	// Prepare
	trampoline = write = alloc_trampoline(to_hook);
	if (trampoline == NULL)
	{
		LOG_ERROR("Failed to allocate a page for trampolines.");
		return NULL;
	}

	// In
	write = write_trampoline_in(trampoline, to_hook, inst_len);
	if (write == NULL)
	{
		LOG_ERROR("Failed to write trampoline in.");
		return NULL;
	}

	// Out
	get_region_info(to_hook, &region_info);
	set_region_write_state(&region_info, 1);
	write = write_trampoline_out(write, to_hook, hook);
	set_region_write_state(&region_info, 0);
	if (write == NULL)
	{
		LOG_ERROR("Failed to write trampoline out.");
		return NULL;
	}

	return trampoline;
}

void* hook_jmp(const char* dll, const char* func_name, void* hook)
{
	void* func_addr;
	void* trampoline;

	// Get the address of the function we're going to hook.
	func_addr = get_proc_addr(dll, func_name);
	if (func_addr == NULL)
	{
		LOG_ERROR("Failed to find function '%s' in '%s'");// , dll, func_name);
		return NULL;
	}

	// Install the hook.
	trampoline = hook_jmp_impl(func_addr, hook);
	if (trampoline == NULL)
	{
		LOG_ERROR("Jump hook failed.");
		return NULL;
	}

	FlushInstructionCache(current_proc(), 0, 0);
	return trampoline;
}


static int apply_hook_jmp_inner(void* self, const hook_decl_t* hook)
{
	void* addr;

	// Hook into a DLL's import by patching the start of the function. 'addr' is
	// the trampoline to call the original. This method doesn't use the IAT.

	addr = hook_jmp(hook->dll, (char*)hook->name_or_addr, hook->hook);
	if (addr == NULL)
	{
		LOG_ERROR("Unable to hook %s in %s");// , hook->name_or_addr, hook->dll);
		return 0;
	}

	// Patch our own IAT with the address of a trampoline that the jmp-style
	// hook creates that calls the original function (i.e. a hook bypass).
	
	if (hook_iat(self, NULL, (char*)hook->name_or_addr, addr, 1) == 0)
	{
		LOG_ERROR("Failed to hook own IAT for %s");// , hook->name_or_addr);
		return 0;
	}
	

	return 1;
}


void apply_hook_jmp(hook_decl_t hook) {
	void* self = get_alloc_base(apply_hook_jmp);

	int ret = apply_hook_jmp_inner(self, &hook);
}

const char* get_kernel_dll()
{
	// We're going to use a different DLL for Win8 (and onwards).

	OSVERSIONINFOEX osvi;
	DWORDLONG mask = 0;
	int op = VER_GREATER_EQUAL;

	ZeroMemory(&osvi, sizeof(OSVERSIONINFOEX));
	osvi.dwOSVersionInfoSize = sizeof(OSVERSIONINFOEX);
	osvi.dwMajorVersion = 6;
	osvi.dwMinorVersion = 2;

	VER_SET_CONDITION(mask, VER_MAJORVERSION, VER_GREATER_EQUAL);
	VER_SET_CONDITION(mask, VER_MINORVERSION, VER_GREATER_EQUAL);

	if (VerifyVersionInfo(&osvi, VER_MAJORVERSION | VER_MINORVERSION, mask))
	{
		return "kernelbase.dll";
	}

	return "kernel32.dll";
}




typedef BOOL(__stdcall *pWriteConsoleW)(
	_In_ HANDLE hConsoleOutput,
	_In_reads_(nNumberOfCharsToWrite) CONST VOID * lpBuffer,
	_In_ DWORD nNumberOfCharsToWrite,
	_Out_opt_ LPDWORD lpNumberOfCharsWritten,
	_Reserved_ LPVOID lpReserved
);
pWriteConsoleW RealWriteConsoleW;

static bool first = true;
static BOOL WINAPI write_console(
	HANDLE handle,
	const wchar_t* buffer,
	DWORD to_write,
	LPDWORD written,
	LPVOID unused
) {
	/*
	int length = lstrlenW(buffer);
	if (length >= 0) {
		char* message = new char[length + 7];
		message[0] = 'w';
		message[1] = 'r';
		message[2] = 'i';
		message[3] = 't';
		message[4] = 'e';
		message[5] = ' ';
		for (int ix = 0; ix < length; ix++) {
			message[ix + 6] = (char)buffer[ix];
		}
		message[length + 6] = '\0';
		
		MessageBoxA(NULL, message, NULL, NULL);
	}
	*/

	BOOL result = RealWriteConsoleW(handle, buffer, to_write, written, unused);
	return result;
}

//http://www.codeproject.com/Articles/14746/Multithreading-Tutorial
//	Uses classes is a good idea for callbacks
class CmdRead {
public:
	HANDLE hConsoleInput;
	CmdRead(HANDLE hConsoleInput) : hConsoleInput(hConsoleInput) { }

private:
	typedef struct {
		wchar_t* lpBuffer;				//out
		DWORD nNumberOfCharsToRead;
		DWORD* lpNumberOfCharsRead;		//out
		PCONSOLE_READCONSOLE_CONTROL pInputControl;

		bool done;
		std::condition_variable cond;
		std::mutex lock;
		BOOL result;
	} ReadRequest;

	//ReadRequests are owned by the requester
	std::queue<ReadRequest*> requests;
	std::condition_variable cond;
	std::mutex lock;

	std::vector<wchar_t> chReadBuffer;

public:
	static unsigned __stdcall StaticReadLoop(void* pThis) {
		((CmdRead*)pThis)->ReadLoop();
		return 1;
	}
	void ReadLoop() {
		//Satisfy requests to underlying ReadConsoleW

		//Read 1 character at a time, pretty sure we can do this. However if we return 1 character at a time it breaks things.
		while (true) {
			CONSOLE_READCONSOLE_CONTROL doesthisevenmatter{ 16, 0, 512, 0 };
			wchar_t chRead;
			DWORD chsRead;
			ReadConsoleW(hConsoleInput, &chRead, 1, &chsRead, &doesthisevenmatter);
			{
				std::unique_lock<std::mutex> lk(lock);
				chReadBuffer.push_back(chRead);
				if (chRead == '\0') {
					int breakhere = 0;
				}
				lk.unlock();
				cond.notify_one();
			}
		}
	}

	void Write(const char* str) {
		std::unique_lock<std::mutex> lk(lock);
		int len = strlen(str) + 1;

		//http://stackoverflow.com/questions/8032080/how-to-convert-char-to-wchar-t
		std::wstring wc(len, L'#');
		size_t we;
		mbstowcs_s(&we, &wc[0], len, str, len);

		DWORD shouldLoopWithThis;
		HANDLE hConsoleOutput = GetStdHandle(STD_OUTPUT_HANDLE);
		WriteConsoleW(hConsoleOutput, wc.c_str(), len, &shouldLoopWithThis, NULL);
		//Don't copy the NULL character, they don't want to read it, it would suggest termination of the stream
		for (int ix = 0; ix < len - 1; ix++)
		{
			chReadBuffer.push_back(str[ix]);
		}
		lk.unlock();
		cond.notify_one();
	}

	static unsigned __stdcall StaticWaitForSufficientData(void* pThis) {
		((CmdRead*)pThis)->WaitForSufficientData();
		return 1;
	}
	void WaitForSufficientData() {
		while(true) {
			ReadRequest* pReq;
			//Get a request
			{
				std::unique_lock<std::mutex> lk(lock);
				cond.wait(lk, [&] { return requests.size() > 0; });
				//Get one request, the loop will come back around to get anymore
				pReq = requests.front();
				requests.pop();
			}

			ReadRequest& req = *pReq;
			//We can mess around with req without locks, we just can't mess around with req.done
			{
				//We want to either satify the request with data from chReadBuffer, or injected characters
				std::unique_lock<std::mutex> lk(lock);
				int newLineIndex = 0;
				cond.wait(lk, [&] {
					//If there is a \n we can use the data
					for (int ix = 0; ix < chReadBuffer.size(); ix++) {
						if (chReadBuffer[ix] == '\n') {
							newLineIndex = ix;
							return true;
						}
					}
					return false;
				});

				int amountToRead = min(newLineIndex + 1, req.nNumberOfCharsToRead);
				*req.lpNumberOfCharsRead = amountToRead;
				for (int ix = 0; ix < amountToRead; ix++) {
					req.lpBuffer[ix] = chReadBuffer[ix];
				}
				chReadBuffer.erase(chReadBuffer.begin(), chReadBuffer.begin() + amountToRead);
			}


			//Finish the request
			{
				std::unique_lock<std::mutex> lkReq(req.lock);
				req.done = true;
				//This number may vary? idk... whatever...
				req.pInputControl->dwCtrlWakeupMask = 3903460080;
				lkReq.unlock();
				req.cond.notify_one();
			}
		}
	}

	//Calls come in from here, were treat these as requests which our thread satisfies
	BOOL callFromInReadConsoleW(
		wchar_t* lpBuffer,				//out
		DWORD nNumberOfCharsToRead,
		DWORD* lpNumberOfCharsRead,		//out
		PCONSOLE_READCONSOLE_CONTROL pInputControl
	) {
		ReadRequest req{ lpBuffer, nNumberOfCharsToRead, lpNumberOfCharsRead, pInputControl, false };
		std::unique_lock<std::mutex> lk(lock);
		std::unique_lock<std::mutex> lkReq(req.lock);
		//Succeeds by default
		req.result = TRUE;
		requests.push(&req);

		//Tell the worker thread we have a request ready
		lk.unlock();
		cond.notify_one();

		//Wait until the request is satisfied
		req.cond.wait(lkReq, [&] { return req.done; });

		//We are done processsing, so we can return
		return req.result;
	}
};

//How can we free these? Lets hope we don't encounter many HANDLES, cause we never free these
std::map<HANDLE, CmdRead*> readers;
std::mutex lockReaders;

bool useWhatever = false;

BOOL readConsoleW(
	HANDLE hConsoleInput,
	wchar_t* lpBuffer,
	DWORD nNumberOfCharsToRead,
	LPDWORD lpNumberOfCharsRead,
	PCONSOLE_READCONSOLE_CONTROL pInputControl
) {
	if (useWhatever) {
		BOOL result = ReadConsoleW(hConsoleInput, lpBuffer, nNumberOfCharsToRead, lpNumberOfCharsRead, pInputControl);
		return result;
	}

	/*
	if (nNumberOfCharsToRead <= 0) return 1;
	wchar_t chRead;
	DWORD chsRead;
	BOOL result = ReadConsoleW(hConsoleInput, &chRead, 1, &chsRead, pInputControl);

	lpBuffer[0] = chRead;
	(*lpNumberOfCharsRead)++;

	return result;
	*/

	//BOOL result = ReadConsoleW(hConsoleInput, lpBuffer, nNumberOfCharsToRead, lpNumberOfCharsRead, pInputControl);
	//return result;

	CmdRead* cmdRead;
	{
		std::unique_lock<std::mutex> lkReaders(lockReaders);
		if (readers.find(hConsoleInput) == readers.end()) {
			cmdRead = readers[hConsoleInput] = new CmdRead(hConsoleInput);
			//We will leak the HANDLE, like we leak CmdRead :D
			_beginthreadex(NULL, 0, CmdRead::StaticReadLoop, cmdRead, NULL, NULL);
			_beginthreadex(NULL, 0, CmdRead::StaticWaitForSufficientData, cmdRead, NULL, NULL);
		}
		cmdRead = readers.find(hConsoleInput)->second;
	}
	BOOL result = cmdRead->callFromInReadConsoleW(lpBuffer, nNumberOfCharsToRead, lpNumberOfCharsRead, pInputControl);
	return result;
}

static unsigned __stdcall WriteTest(void*nothing) {
	int delay = 1000;
	while (true) {
		Sleep(delay);
		{
			std::unique_lock<std::mutex> lkReaders(lockReaders);
			if (readers.size() == 0) continue;
			delay = 1000 * 15;
			auto i = readers.begin();
			int index = 0;
			for (; i != readers.end(); i++) {
				index--;
				if (index <= 0) break;
			}
			CmdRead* cmdRead = i->second;
			cmdRead->Write("hello\n");
		}
	}
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID unused) {
	if (reason != DLL_PROCESS_ATTACH) return TRUE;

	MessageBoxA(NULL, "inside injected", NULL, NULL);

	_beginthreadex(NULL, 0, WriteTest, NULL, NULL, NULL);

	//Hmm... do we need to throw an vectored exception, catch it, and do this work in there?
	//	Maybe... but I will wait until I have to.

	//http://www.codeproject.com/Articles/43694/Forbidding-the-Clipboard-for-the-specified-process
	//	Would work... but may not be very portable (between versions of windows)

	/*
	{
		RealGetClipboardData = GetClipboardData;
		hook_decl_t hook;
		hook.type = HOOK_TYPE_IAT_BY_NAME;
		hook.base = GetModuleHandle(NULL);
		hook.dll = NULL;
		hook.name_or_addr = "GetClipboardData";
		hook.hook = write_console;
		(pWriteConsoleW)apply_hook(hook);
	}
	*/

	{
		RealWriteConsoleW = WriteConsoleW;
		hook_decl_t hook;
		hook.type = HOOK_TYPE_IAT_BY_NAME;
		hook.base = GetModuleHandle(NULL);
		hook.dll = NULL;
		hook.name_or_addr = "WriteConsoleW";
		hook.hook = write_console;
		apply_hook_iat(hook);
	}

	{
		hook_decl_t hook;
		hook.type = HOOK_TYPE_JMP;
		hook.base = NULL;
		hook.dll = get_kernel_dll();
		hook.name_or_addr = "ReadConsoleW";
		hook.hook = readConsoleW;
		apply_hook_jmp(hook);
	}
	
	return 1;
}