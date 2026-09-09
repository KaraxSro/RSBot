#include <Windows.h>
#include <DbgHelp.h>
#include <stdio.h>
#include <string>
#include <vector>
#include <iostream>
#include <sstream>
#include <fstream>
#include <iphlpapi.h>
#include "detours.h"
#include "PayloadHelper.h"

#pragma comment(lib, "IPHLPAPI.lib")
#pragma comment(lib, "ws2_32.lib")

using namespace std;

HMODULE g_Instance = NULL;
HANDLE g_Thread = NULL;

BYTE g_IsDebug;
string g_RedirectIP = "127.0.0.1";
WORD g_RedirectPort = 1500;
bool g_Activated = false;

vector<string> g_RealGatewayAddresses;
WORD g_RealGatewayPort = 15779;
string g_SessionId;
string g_LaunchId;
string g_LogPath;
LPTOP_LEVEL_EXCEPTION_FILTER g_PreviousExceptionFilter = NULL;

void NativeLog(const string& stage, const string& message)
{
	if (g_LogPath.empty()) return;
	try
	{
		SYSTEMTIME now;
		GetLocalTime(&now);
		ofstream stream(g_LogPath, ios::out | ios::app);
		if (!stream.is_open()) return;
		stream << "[" << now.wYear << "-";
		stream.width(2); stream.fill('0'); stream << now.wMonth << "-";
		stream.width(2); stream << now.wDay << " ";
		stream.width(2); stream << now.wHour << ":";
		stream.width(2); stream << now.wMinute << ":";
		stream.width(2); stream << now.wSecond << ".";
		stream.width(3); stream << now.wMilliseconds << "] [Native] [PID:" << GetCurrentProcessId()
			<< "] [Session:" << g_SessionId << "] [ClientLaunch:" << g_LaunchId << "] [" << stage << "] " << message << endl;
	}
	catch (...) { }
}

string GetCrashDumpPath()
{
	string directory = ".";
	size_t separator = g_LogPath.find_last_of("\\/");
	if (separator != string::npos)
		directory = g_LogPath.substr(0, separator);
	directory += "\\CrashDumps";
	CreateDirectoryA(directory.c_str(), NULL);

	SYSTEMTIME now;
	GetLocalTime(&now);
	char fileName[MAX_PATH] = { 0 };
	_snprintf_s(
		fileName,
		_countof(fileName),
		_TRUNCATE,
		"%s\\sro_client_%s_%lu_%04u%02u%02u_%02u%02u%02u.dmp",
		directory.c_str(),
		g_LaunchId.empty() ? "unknown" : g_LaunchId.c_str(),
		GetCurrentProcessId(),
		now.wYear,
		now.wMonth,
		now.wDay,
		now.wHour,
		now.wMinute,
		now.wSecond
	);
	return fileName;
}

LONG WINAPI ClientUnhandledExceptionFilter(EXCEPTION_POINTERS* exceptionPointers)
{
	DWORD exceptionCode = exceptionPointers && exceptionPointers->ExceptionRecord
		? exceptionPointers->ExceptionRecord->ExceptionCode
		: 0;
	void* exceptionAddress = exceptionPointers && exceptionPointers->ExceptionRecord
		? exceptionPointers->ExceptionRecord->ExceptionAddress
		: NULL;

	stringstream details;
	details << "Unhandled exception; code=0x" << hex << exceptionCode
		<< "; address=" << exceptionAddress
		<< "; thread=" << dec << GetCurrentThreadId();
	NativeLog("crash", details.str());

	string dumpPath = GetCrashDumpPath();
	bool dumpWritten = false;
	DWORD dumpError = ERROR_SUCCESS;
	HMODULE dbgHelp = LoadLibraryA("dbghelp.dll");
	if (dbgHelp)
	{
		auto miniDumpWriteDump = reinterpret_cast<decltype(&MiniDumpWriteDump)>(
			GetProcAddress(dbgHelp, "MiniDumpWriteDump")
		);
		if (miniDumpWriteDump)
		{
			HANDLE dumpFile = CreateFileA(
				dumpPath.c_str(),
				GENERIC_WRITE,
				FILE_SHARE_READ,
				NULL,
				CREATE_ALWAYS,
				FILE_ATTRIBUTE_NORMAL,
				NULL
			);
			if (dumpFile != INVALID_HANDLE_VALUE)
			{
				MINIDUMP_EXCEPTION_INFORMATION exceptionInformation = {
					GetCurrentThreadId(),
					exceptionPointers,
					FALSE
				};
				dumpWritten = miniDumpWriteDump(
					GetCurrentProcess(),
					GetCurrentProcessId(),
					dumpFile,
					static_cast<MINIDUMP_TYPE>(MiniDumpNormal | MiniDumpWithThreadInfo),
					exceptionPointers ? &exceptionInformation : NULL,
					NULL,
					NULL
				) == TRUE;
				if (!dumpWritten)
					dumpError = GetLastError();
				CloseHandle(dumpFile);
			}
			else
			{
				dumpError = GetLastError();
			}
		}
		else
		{
			dumpError = GetLastError();
		}
		FreeLibrary(dbgHelp);
	}
	else
	{
		dumpError = GetLastError();
	}

	NativeLog(
		"crash",
		dumpWritten
			? string("Minidump written: ") + dumpPath
			: string("Minidump failed; win32=") + to_string(dumpError)
	);

	if (g_PreviousExceptionFilter && g_PreviousExceptionFilter != ClientUnhandledExceptionFilter)
		return g_PreviousExceptionFilter(exceptionPointers);

	return EXCEPTION_CONTINUE_SEARCH;
}

std::vector<std::string> TokenizeString(const std::string& str, const std::string& delim)
{
	// http://www.gamedev.net/community/forums/topic.asp?topic_id=381544#TokenizeString
	using namespace std;
	vector<string> tokens;
	size_t p0 = 0, p1 = string::npos;
	while (p0 != string::npos)
	{
		p1 = str.find_first_of(delim, p0);
		if (p1 != p0)
		{
			string token = str.substr(p0, p1 - p0);
			tokens.push_back(token);
		}
		p0 = str.find_first_not_of(delim, p1);
	}
	return tokens;
}

extern "C" HANDLE(WINAPI* Real_CreateSemaphoreW)(
	LPSECURITY_ATTRIBUTES lpSemaphoreAttributes,
	LONG lInitialCount,
	LONG lMaximumCount,
	LPCWSTR lpName) = CreateSemaphoreW;

HANDLE WINAPI User_CreateSemaphoreW(
	LPSECURITY_ATTRIBUTES lpSemaphoreAttributes,
	LONG lInitialCount,
	LONG lMaximumCount,
	LPCWSTR lpName)
{
	if (lpName && wcsstr(lpName, L"Silkroad Client") != nullptr)
	{
		wchar_t newName[128] = { 0 };

		_snwprintf_s(
			newName,
			_countof(newName),
			_TRUNCATE,
			L"%s_%llu",
			lpName,
			(unsigned long long)(__rdtsc() & 0xFFFFFFFF)
		);

		wprintf(L"%s %s", __FUNCTION__, newName);

		return Real_CreateSemaphoreW(
			lpSemaphoreAttributes,
			lInitialCount,
			lMaximumCount,
			newName
		);
	}

	return Real_CreateSemaphoreW(
		lpSemaphoreAttributes,
		lInitialCount,
		lMaximumCount,
		lpName
	);
}

extern "C" HANDLE(WINAPI* Real_CreateSemaphoreA)(
	LPSECURITY_ATTRIBUTES lpSemaphoreAttributes,
	LONG lInitialCount,
	LONG lMaximumCount,
	LPCSTR lpName) = CreateSemaphoreA;

HANDLE WINAPI User_CreateSemaphoreA(
	LPSECURITY_ATTRIBUTES lpSemaphoreAttributes,
	LONG lInitialCount,
	LONG lMaximumCount,
	LPCSTR lpName)
{
	if (lpName && strstr(lpName, "Silkroad Client") != nullptr)
	{
		char newName[128] = { 0 };

		_snprintf_s(
			newName,
			_countof(newName),
			_TRUNCATE,
			"%s_%llu",
			lpName,
			(unsigned long long)(__rdtsc() & 0xFFFFFFFF)
		);

		printf("%s %s", __FUNCTION__, newName);

		return Real_CreateSemaphoreA(
			lpSemaphoreAttributes,
			lInitialCount,
			lMaximumCount,
			newName
		);
	}

	return Real_CreateSemaphoreA(
		lpSemaphoreAttributes,
		lInitialCount,
		lMaximumCount,
		lpName
	);
}

extern "C" HANDLE(WINAPI* Real_CreateMutexA)(LPSECURITY_ATTRIBUTES lpMutexAttributes, BOOL bInitialOwner, LPCSTR lpName) = CreateMutexA;
HANDLE WINAPI User_CreateMutexA(LPSECURITY_ATTRIBUTES lpMutexAttributes, BOOL bInitialOwner, LPCSTR lpName)
{

	if (lpName && strstr(lpName, "Silkroad Client") != nullptr)
	{
		char newName[128] = { 0 };

		_snprintf_s(
			newName,
			_countof(newName),
			_TRUNCATE,
			"%s_%llu",
			lpName,
			(unsigned long long)(__rdtsc() & 0xFFFFFFFF)
		);
		printf("%s %s", __FUNCTION__, newName);

		return Real_CreateMutexA(lpMutexAttributes, bInitialOwner, newName);
	}
	return Real_CreateMutexA(lpMutexAttributes, bInitialOwner, lpName);
}

extern "C" int (WINAPI* Real_bind)(SOCKET s, const struct sockaddr* name, int namelen) = bind;
int WINAPI User_bind(SOCKET s, const struct sockaddr* name, int namelen)
{
	if (name && namelen == 16)
	{
		sockaddr_in* inaddr = (sockaddr_in*)name;
		if (inaddr->sin_port == ntohs(g_RealGatewayPort))
		{
			return 0;
		}
	}
	return Real_bind(s, name, namelen);
}

extern "C" DWORD(WINAPI* Real_GetAdaptersInfo)(PIP_ADAPTER_INFO pAdapterInfo, PULONG pOutBufLen) = GetAdaptersInfo;
DWORD WINAPI User_GetAdaptersInfo(PIP_ADAPTER_INFO pAdapterInfo, PULONG pOutBufLen)
{
	DWORD dwResult = Real_GetAdaptersInfo(pAdapterInfo, pOutBufLen);
	if (dwResult == ERROR_SUCCESS)
	{
		PIP_ADAPTER_INFO pAdapter = pAdapterInfo;
		srand(__rdtsc() & 0xFFFFFFFF);
		while (pAdapter)
		{
			for (UINT i = 1; i < pAdapter->AddressLength; i++)
			{
				printf("%.2X -> ", pAdapter->Address[i]);
				pAdapter->Address[i] = rand() % 256;
				printf("%.2X ", pAdapter->Address[i]);
			}
			printf("\n");
			pAdapter = pAdapter->Next;
		}
	}
	return dwResult;
}

extern "C" int (WINAPI* Real_connect)(SOCKET, const struct sockaddr*, int) = connect;
int WINAPI Detour_connect(SOCKET s, const struct sockaddr* name, int len)
{
	auto* editing = reinterpret_cast<sockaddr_in*>(const_cast<sockaddr*>(name));

	printf("[Detour_connect] ip: %s, port: %d \n", inet_ntoa(editing->sin_addr), htons(editing->sin_port));

	for (auto& gatewayAddress : g_RealGatewayAddresses)
	{
		struct hostent* remoteHost = gethostbyname(gatewayAddress.c_str());
		if (remoteHost == NULL || remoteHost->h_addr_list[0] == NULL) continue;

		struct in_addr maddr = { 0 };
		maddr.s_addr = *(u_long*)gethostbyname(gatewayAddress.c_str())->h_addr_list[0];
		if (strcmp(inet_ntoa(editing->sin_addr), inet_ntoa(maddr)) == 0 && htons(editing->sin_port) == g_RealGatewayPort)
		{

			editing->sin_addr.S_un.S_addr = inet_addr(g_RedirectIP.c_str());
			editing->sin_port = htons(g_RedirectPort);

			printf("[connect] redirected gateway %s\n", g_RedirectIP.c_str());
		}
	}

	// Regular connect
	return Real_connect(s, name, len);
}

void Install()
{
	NativeLog("install", "BEGIN hook installation");
	CreateMutexA(0, FALSE, "Silkroad Online Launcher");
	CreateMutexA(0, FALSE, "Ready");

	WSADATA wsaData = { 0 };
	int wsaResult = WSAStartup(MAKEWORD(2, 2), &wsaData);
	NativeLog("winsock", string("WSAStartup result=") + to_string(wsaResult));

	BOOL restoreResult = DetourRestoreAfterWith();
	LONG beginResult = DetourTransactionBegin();
	LONG updateResult = DetourUpdateThread(GetCurrentThread());
	NativeLog("detours", string("restore=") + to_string(restoreResult) + "; begin=" + to_string(beginResult) + "; updateThread=" + to_string(updateResult));

	//Multiclient
	LONG attachMutex = DetourAttach(&(PVOID&)Real_CreateMutexA, User_CreateMutexA);
	LONG attachBind = DetourAttach(&(PVOID&)Real_bind, User_bind);
	LONG attachAdapters = DetourAttach(&(PVOID&)Real_GetAdaptersInfo, User_GetAdaptersInfo);
	LONG attachSemaphoreA = DetourAttach(&(PVOID&)Real_CreateSemaphoreA, User_CreateSemaphoreA);
	LONG attachSemaphoreW = DetourAttach(&(PVOID&)Real_CreateSemaphoreW, User_CreateSemaphoreW);
	LONG attachConnect = DetourAttach(&(PVOID&)Real_connect, Detour_connect);
	NativeLog("detours", string("attach results: mutex=") + to_string(attachMutex) + "; bind=" + to_string(attachBind) + "; adapters=" + to_string(attachAdapters) + "; semaphoreA=" + to_string(attachSemaphoreA) + "; semaphoreW=" + to_string(attachSemaphoreW) + "; connect=" + to_string(attachConnect));

	LONG commitResult = DetourTransactionCommit();
	NativeLog("detours", string("commit=") + to_string(commitResult));
	WSACleanup();
	NativeLog("install", commitResult == NO_ERROR ? "END success" : "END failed");
}

void Uninstall()
{
	DetourRestoreAfterWith();
	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());

	//Multiclient
	DetourDetach(&(PVOID&)Real_CreateMutexA, User_CreateMutexA);
	DetourDetach(&(PVOID&)Real_bind, User_bind);
	DetourDetach(&(PVOID&)Real_GetAdaptersInfo, User_GetAdaptersInfo);
	DetourDetach(&(PVOID&)Real_CreateSemaphoreA, User_CreateSemaphoreA);

	DetourTransactionCommit();
}

void LoadConfig()
{
	char* tempFolder = NULL;
	_dupenv_s(&tempFolder, NULL, "TMP");
	if (!tempFolder) return;

	stringstream payloadPath;
	payloadPath << tempFolder << "\\RSBot_" << GetCurrentProcessId() << ".tmp";

	for (int i = 0; i < 30; i++) {
		ifstream stream(payloadPath.str(), ifstream::binary);
		if (stream.is_open()) {
			g_Activated = true;
			PayloadRead(stream, g_IsDebug);
			PayloadReadString(stream, g_RedirectIP);
			PayloadRead(stream, g_RedirectPort);

			DWORD nRealGatewayAddressCount = 0;
			PayloadRead(stream, nRealGatewayAddressCount);
			for (size_t j = 0; j < nRealGatewayAddressCount; j++)
			{
				string address;
				PayloadReadString(stream, address);
				g_RealGatewayAddresses.push_back(address);
			}
			PayloadRead(stream, g_RealGatewayPort);
			PayloadReadString(stream, g_SessionId);
			PayloadReadString(stream, g_LaunchId);
			PayloadReadString(stream, g_LogPath);

			stream.close();
			NativeLog("config", string("Configuration parsed; gatewayCount=") + to_string(nRealGatewayAddressCount));
			DeleteFileA(payloadPath.str().c_str());
			NativeLog("config", "Temporary configuration deleted");
			return;
		}
		Sleep(100);
	}
}

DWORD WINAPI Initialize(LPVOID lpParam) {
	LoadConfig();

	if (!g_Activated) {
		return 0;
	}
	NativeLog("initialize", "DLL attached; initialization worker started");
	g_PreviousExceptionFilter = SetUnhandledExceptionFilter(ClientUnhandledExceptionFilter);
	NativeLog("initialize", "Unhandled exception filter installed");

	if (g_IsDebug) {
		AllocConsole();
		freopen_s((FILE**)stdout, "CONOUT$", "w", stdout);
	}

	Install();
	NativeLog("initialize", "Initialization worker completed");
	return 0;
}

extern "C" _declspec(dllexport) BOOL APIENTRY DllMain(HMODULE hModule, DWORD ulReason, LPVOID lpReserved)
{
	if (ulReason == DLL_PROCESS_ATTACH) {
		g_Instance = hModule;
		DisableThreadLibraryCalls(hModule);
		CloseHandle(CreateThread(NULL, 0, Initialize, NULL, 0, NULL));
	}
	return TRUE;
}
