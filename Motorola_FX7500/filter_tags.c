#include <stdio.h>
#include <string.h>
#include <windows.h>
#include "rfidapi.h"
#include "rfidapiStructs.h"

static void epc_to_hex(const UINT8* data, UINT32 len, char* out, size_t outsz)
{
    size_t need = (size_t)len * 2 + 1;
    if (outsz < need) {
        len = (UINT32)((outsz - 1) / 2);
    }
    for (UINT32 i = 0; i < len; ++i) {
        sprintf_s(out + (i * 2), outsz - (i * 2), "%02X", data[i]);
    }
    out[len * 2] = '\0';
}

int main(int argc, char** argv)
{
    SetConsoleOutputCP(65001);

    if (argc < 2) {
        printf("Uso: %s <IP_LECTOR>\n", argv[0]);
        return 1;
    }
    const char* HOST = argv[1];
    const UINT32 PORT = 5084;
    const UINT32 TIMEOUT_MS = 5000;
    const char* PREFIJO = "E280B120";

    RFID_HANDLE32 h = NULL;
    CONNECTION_INFO ci = { 0 };
    ci.version = RFID_API3_5_1;
    ci.lpSecConInfo = NULL;
    ci.lpReserved[0] = NULL;
    ci.lpReserved[1] = NULL;
    ci.lpReserved[2] = NULL;

    printf("[INFO] Conectando a %s:%u...\n", HOST, PORT);
    int rc = RFID_ConnectA(&h, (char*)HOST, PORT, TIMEOUT_MS, &ci);
    if (rc != RFID_API_SUCCESS) {
        printf("[ERR] RFID_ConnectA rc=%d\n", rc);
        return 1;
    }
    printf("[OK] Conectado a %s:%u\n", HOST, PORT);

    RFID_SetTraceLevel(h, TRACE_LEVEL_ALL);

    TRIGGER_INFO triggerInfo = { 0 };
    triggerInfo.startTrigger.type = 0;
    triggerInfo.stopTrigger.type = 0;
    triggerInfo.stopTrigger.value.duration = 10000;

    rc = RFID_PerformInventory(h, NULL, NULL, NULL, NULL);
    if (rc != RFID_API_SUCCESS) {
        printf("[ERR] RFID_PerformInventory rc=%d\n", rc);
        RFID_Disconnect(h);
        return 2;
    }
    printf("[OK] Inventario iniciado. Acerca un tag a la antena...\n");

    TAG_DATA* tag = RFID_AllocateTag(h);
    if (!tag) {
        printf("[ERR] RFID_AllocateTag\n");
        RFID_StopInventory(h);
        RFID_Disconnect(h);
        return 3;
    }

    FILE* f = NULL;
    fopen_s(&f, "tags_prefijo.csv", "w");
    if (f) fprintf(f, "timestamp,epc,antenna,rssi\n");

    const ULONGLONG RUN_MS = 10000;
    ULONGLONG t0 = GetTickCount64();

    while (GetTickCount64() - t0 < RUN_MS) {
        while (RFID_API_SUCCESS == RFID_GetReadTag(h, tag)) {
            char epc[512];
            epc_to_hex(tag->pTagID, tag->tagIDLength, epc, sizeof(epc));
            if (strncmp(epc, PREFIJO, strlen(PREFIJO)) == 0) {
                // Timestamp en ms desde epoch
                FILETIME ft;
                ULARGE_INTEGER ull;
                GetSystemTimeAsFileTime(&ft);
                ull.LowPart = ft.dwLowDateTime;
                ull.HighPart = ft.dwHighDateTime;
                const ULONGLONG EPOCH_DIFF = 11644473600000ULL;
                unsigned long long timestamp_ms = (ull.QuadPart / 10000ULL) - EPOCH_DIFF;

                printf("%llu | EPC: %s | Antena: %u | RSSI: %d dBm\n",
                    timestamp_ms, epc, (unsigned)tag->antennaID, (int)tag->peakRSSI);

                if (f) {
                    fprintf(f, "%llu,%s,%u,%d\n",
                        timestamp_ms, epc, (unsigned)tag->antennaID, (int)tag->peakRSSI);
                    fflush(f);
                }
            }
        }
        Sleep(50);
    }

    if (f) fclose(f);
    RFID_DeallocateTag(h, tag);
    RFID_StopInventory(h);
    RFID_Disconnect(h);

    printf("[OK] Desconectado.\n");
    return 0;
}