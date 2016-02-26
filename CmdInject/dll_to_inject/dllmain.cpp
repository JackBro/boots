#include "hooking.h"

// dllmain.cpp : Defines the entry point for the DLL application.
#include <Windows.h>
#include <process.h>
#include <vector>

#include <iostream>
#include <string>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <queue>
#include <map>


typedef BOOL(__stdcall *pWriteConsoleW)(
	_In_ HANDLE hConsoleOutput,
	_In_reads_(nNumberOfCharsToWrite) CONST VOID * lpBuffer,
	_In_ DWORD nNumberOfCharsToWrite,
	_Out_opt_ LPDWORD lpNumberOfCharsWritten,
	_Reserved_ LPVOID lpReserved
);
pWriteConsoleW RealWriteConsoleW;


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
	std::vector<ReadRequest*> requests;
	std::condition_variable cond;
	std::mutex lock;

	typedef struct {
		ULONG value;
		DWORD length;
	} ValueLength;
	//std::vector<wchar_t> chReadBuffer;
	//std::vector<ValueLength> chReadBuffer_dwControlKeyState;

public:
	static unsigned __stdcall StaticReadLoop(void* pThis) {
		((CmdRead*)pThis)->ReadLoop();
		return 1;
	}
	void ReadLoop() {
		//Satisfy requests to underlying ReadConsoleW

		//Read 1 character at a time, pretty sure we can do this. However if we return 1 character at a time it breaks things.
		while (true) {
			CONSOLE_READCONSOLE_CONTROL readConsoleControl{ 16, 0, 512, 0 };
			DWORD chsRead;

			//TODO: Should really return the value of the RESULT
			wchar_t chRead[8096];
			BOOL result = ReadConsoleW(hConsoleInput, &chRead, sizeof_array(chRead), &chsRead, &readConsoleControl);

			TakeMoreData(chRead, chsRead, readConsoleControl, result);
		}
	}

	//This could mess things up, if we are in ReadConsoleW and we inject stuff, it may create a situation where ReadConsoleW
	//	is being called when the cmd doesn't want to call it anymore. But oh well..
	void Write(const char* str) {
		DWORD len = strlen(str) + 1;

		//http://stackoverflow.com/questions/8032080/how-to-convert-char-to-wchar-t
		std::wstring wc(len, L'#');
		size_t we;
		mbstowcs_s(&we, &wc[0], len, str, len);

		const wchar_t* chs = wc.c_str();

		//We no longer want the null terminator at this point, as it would suggest termination of the stream
		len--;

		HANDLE hConsoleOutput = GetStdHandle(STD_OUTPUT_HANDLE);
		
		CONSOLE_READCONSOLE_CONTROL readConsoleControl{ 16, 0, 512, 3903460080 };
		TakeMoreData(chs, len, readConsoleControl, TRUE);

		DWORD amountWritten = 0;
		while (amountWritten < len)
		{
			DWORD amountWrittenLast = 0;
			WriteConsoleW(hConsoleOutput, chs + amountWritten, len - amountWritten, &amountWrittenLast, NULL);
			amountWritten += amountWrittenLast;
		}
	}

	/*
wchar_t* lpBuffer;				//out
DWORD nNumberOfCharsToRead;
DWORD* lpNumberOfCharsRead;		//out
PCONSOLE_READCONSOLE_CONTROL pInputControl;

bool done;
std::condition_variable cond;
std::mutex lock;
BOOL result;
	*/

	//Consumer of requests. Call when you have data available. So we could have multiple consumers of requests.
	//	However makes it so producers don't need to be thread safe (cause I am fairly sure they don't really need to be).
	void TakeMoreData(
		const wchar_t* chs,
		DWORD chsLength,
		CONSOLE_READCONSOLE_CONTROL consoleResult,
		BOOL result
	) {
		DWORD amountRead = 0;

		while (chsLength > 0)
		{
			std::unique_lock<std::mutex> lk(lock);
			//Wait until we have a request
			cond.wait(lk, [&] { return requests.size() > 0; });

			//Get one request, the loop will come back around to get anymore
			ReadRequest& req = *requests.front();
			requests.erase(requests.begin());

			std::unique_lock<std::mutex> lkReq(req.lock);

			//Read characters
			int amountToRead = min(chsLength, req.nNumberOfCharsToRead);
			*req.lpNumberOfCharsRead = amountToRead;
			for (int ix = 0; ix < amountToRead; ix++) {
				req.lpBuffer[ix] = chs[ix];
			}
			chs += amountToRead;
			chsLength -= amountToRead;
			amountRead += amountToRead;

			*req.pInputControl = consoleResult;
			req.result = result;
			req.done = true;
			lkReq.unlock();
			req.cond.notify_one();
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
		requests.push_back(&req);

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
	//return ReadConsoleW(hConsoleInput, lpBuffer, nNumberOfCharsToRead, lpNumberOfCharsRead, pInputControl);

	CmdRead* cmdRead;
	{
		std::unique_lock<std::mutex> lkReaders(lockReaders);
		if (readers.find(hConsoleInput) == readers.end()) {
			cmdRead = readers[hConsoleInput] = new CmdRead(hConsoleInput);
			//We will leak the HANDLE, like we leak CmdRead :D
			_beginthreadex(NULL, 0, CmdRead::StaticReadLoop, cmdRead, NULL, NULL);
		}
		cmdRead = readers.find(hConsoleInput)->second;
	}
	BOOL result = cmdRead->callFromInReadConsoleW(lpBuffer, nNumberOfCharsToRead, lpNumberOfCharsRead, pInputControl);
	return result;
}

static BOOL WINAPI write_console(
	HANDLE handle,
	const wchar_t* buffer,
	DWORD to_write,
	LPDWORD written,
	LPVOID unused
	) {
	BOOL result = RealWriteConsoleW(handle, buffer, to_write, written, unused);
	return result;
}

static unsigned __stdcall WriteTest(void*nothing) {
	int delay = 1000;
	while (true) {
		Sleep(delay);
		{
			CmdRead* cmdRead;
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
				cmdRead = i->second;
			}
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
		hook.name_or_addr = "WriteConsoleW";
		hook.hook = write_console;
		apply_hook_iat(hook);
	}

	{
		hook_decl_t hook;
		hook.type = HOOK_TYPE_JMP;
		hook.base = NULL;
		hook.name_or_addr = "ReadConsoleW";
		hook.hook = readConsoleW;
		apply_hook_jmp(hook);
	}
	
	return 1;
}