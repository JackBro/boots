#pragma once

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

void apply_hook_jmp(hook_decl_t hook);

void* apply_hook_iat(hook_decl_t hook);

#define sizeof_array(x) (sizeof((x)) / sizeof((x)[0]))

#include <Windows.h>

static void LOG_ERROR(const char* str) {
	MessageBoxA(0, str, 0, 0);
	throw str;
}