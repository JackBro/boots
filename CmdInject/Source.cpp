#include "Windows.h"
#include "TlHelp32.h"
#include "Psapi.h"

static void LOG_ERROR(const char* str) {
	MessageBox(NULL, str, NULL, NULL);
	throw str;
}

static void toggle_threads(DWORD pid, int on)
{
	BOOL ok;
	HANDLE th32;
	THREADENTRY32 te = { sizeof(te) };

	th32 = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, pid);
	if (th32 == INVALID_HANDLE_VALUE)
	{
		return;
	}

	ok = Thread32First(th32, &te);
	while (ok != FALSE)
	{
		HANDLE thread;

		if (te.th32OwnerProcessID != pid)
		{
			ok = Thread32Next(th32, &te);
			continue;
		}

		thread = OpenThread(THREAD_ALL_ACCESS, FALSE, te.th32ThreadID);
		//Hmm... idk...
		if (!thread) break;
		if (on)
		{
			ResumeThread(thread);
		}
		else
		{
			SuspendThread(thread);
		}
		CloseHandle(thread);

		ok = Thread32Next(th32, &te);
	}

	CloseHandle(th32);
}

int do_inject_impl(DWORD target_pid, const char* dll_path)
{
	int e1 = GetLastError();
	// Open the process so we can operate on it.
	HANDLE target_process = OpenProcess(
		PROCESS_QUERY_INFORMATION |
		PROCESS_CREATE_THREAD |
		PROCESS_VM_OPERATION |
		PROCESS_VM_WRITE |
		PROCESS_VM_READ,
		FALSE,
		target_pid
	);
	if (target_process == NULL)
	{
		int e2 = GetLastError();
		LOG_ERROR("Failed to open target process.");
		return 0;
	}

	// Check arch matches.
	BOOL is_wow_64[2];
	IsWow64Process(target_process, is_wow_64);
	IsWow64Process(GetCurrentProcess(), is_wow_64 + 1);
	if (is_wow_64[0] != is_wow_64[1])
	{
		if (is_wow_64[0] && !is_wow_64[1]) {
			LOG_ERROR("We are 64 bit while target process is 32 bit.");
		}
		else {
			LOG_ERROR("We are 64 bit while target process is 32 bit.");
		}
		

		LOG_ERROR("32/64-bit mismatch. Use loader executable that matches parent architecture.");
		return 0;
	}

	// We'll use LoadLibraryA as the entry point for out remote thread.
	void* thread_proc = LoadLibraryA;
	LPVOID thread_proc2 = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
	if (thread_proc == NULL || thread_proc != thread_proc2)
	{
		LOG_ERROR("Failed to find LoadLibraryA address");
		return 0;
	}

	// Create a buffer in the process to write data to.
	LPVOID buffer = VirtualAllocEx(
		target_process,
		NULL,
		sizeof(dll_path),
		MEM_COMMIT,
		PAGE_READWRITE
		);
	if (buffer == NULL)
	{
		LOG_ERROR("VirtualAllocEx failed");
		return 0;
	}

	// Tell remote process what DLL to load.
	BOOL t = WriteProcessMemory(
		target_process,
		buffer,
		dll_path,
		strlen(dll_path) + 1,
		NULL
		);
	if (t == FALSE)
	{
		LOG_ERROR("WriteProcessMemory() failed");
		return 0;
	}

	//LOG_INFO("Creating remote thread at %p with parameter %p", thread_proc, buffer);

	// Disable threads and create a remote thread.
	toggle_threads(target_pid, 0);
	DWORD thread_id;
	HANDLE remote_thread = CreateRemoteThread(
		target_process,
		NULL,
		0,
		(LPTHREAD_START_ROUTINE)thread_proc,
		buffer,
		0,
		&thread_id
		);
	if (remote_thread == NULL)
	{
		LOG_ERROR("CreateRemoteThread() failed");
		return 0;
	}

	// Wait for injection to complete.
	WaitForSingleObject(remote_thread, 1000);
	DWORD thread_ret;
	GetExitCodeThread(remote_thread, &thread_ret);
	int ret = !!thread_ret;

	toggle_threads(target_pid, 1);

	// Clean up and quit
	CloseHandle(remote_thread);
	VirtualFreeEx(target_process, buffer, 0, MEM_RELEASE);
	CloseHandle(target_process);

	if (!ret) {
		LOG_ERROR("Failed to inject DLL");
	}

	return ret;
}

//7684

#include <memory>

int getCmdProcId() {
	PROCESSENTRY32 entry;
	entry.dwSize = sizeof(PROCESSENTRY32);

	const std::shared_ptr<void> snapshot
		(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, NULL), CloseHandle);

	if (Process32First(snapshot.get(), &entry) == TRUE)
	{
		while (Process32Next(snapshot.get(), &entry) == TRUE)
		{
			if (_stricmp(entry.szExeFile, "cmd.exe") != 0) continue;
			
			//HANDLE hProcess = OpenProcess(PROCESS_ALL_ACCESS, FALSE, entry.th32ProcessID);
			//CloseHandle(hProcess);

			return entry.th32ProcessID;
		}
	}
}

int main() {
	int proc_id = getCmdProcId();
	int result = do_inject_impl(proc_id, "C:\\Users\\quentin.brooks\\Dropbox\\boots\\CmdInject\\x64\\Debug\\dll_to_inject.dll");
	return result;
}