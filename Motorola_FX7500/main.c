/* fx_read_10s_debug.c : Conecta → inventario 10s con diagnóstico → desconecta */

#include <stdio.h>
#include <string.h>
#include <windows.h>
#include "rfidapi.h"
#include "rfidapiStructs.h"

/* EPC binario -> HEX (seguro) */
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

// Plan en pseudocódigo:
// 1. Incluir <stdbool.h> para definir el tipo bool.
// 2. Declarar la función WriteTag como static para que sea de ámbito local al archivo.
// 3. No eliminar ningún código existente, solo modificar la declaración de la función y agregar el include necesario.

// Agregar al inicio del archivo, después de los includes existentes:
#include <stdbool.h>

// Cambiar la declaración de la función WriteTag:
static bool WriteTag(RFID_HANDLE32 h, UINT8* pTagID, UINT32 tagIDLength, MEMORY_BANK mb, UINT16 offset, UINT32 password, UINT8* pData, UINT16 dataLength) {
    WRITE_ACCESS_PARAMS params = {0};
    params.memoryBank = mb;
    params.byteOffset = offset;
    params.pWriteData = pData;
    params.writeDataLength = dataLength;
    params.accessPassword = password;
    RFID_STATUS rc = RFID_Write(h, pTagID, tagIDLength, &params, NULL, NULL, NULL);
    return rc == RFID_API_SUCCESS;
}

static bool ReadTagWord(RFID_HANDLE32 h, UINT8* pTagID, UINT32 tagIDLength, MEMORY_BANK mb, UINT16 offset, UINT8* pData, UINT16 dataLength) {
    READ_ACCESS_PARAMS params = {0};
    params.memoryBank = mb;
    params.byteOffset = offset;
    params.byteCount = dataLength;
    params.accessPassword = 0;
    TAG_DATA tagData = {0};
    RFID_STATUS rc = RFID_Read(h, pTagID, tagIDLength, &params, NULL, NULL, &tagData, NULL);
    if (rc == RFID_API_SUCCESS && tagData.memoryBankDataLength >= dataLength) {
        memcpy(pData, tagData.pMemoryBankData, dataLength);
        return true;
    }
    return false;
}

int main(int argc, char** argv)
{
    SetConsoleOutputCP(65001); // Fuerza UTF-8 en la consola de Windows
    // Usa solo ASCII en los mensajes para evitar problemas de compatibilidad

    if (argc < 3) {
        printf("Uso: %s <IP_LECTOR> <EPC_OBJETIVO> [TIEMPO_SEGUNDOS]\n", argv[0]);
        return 1;
    }
    const char* HOST = argv[1];
    const char* EPC_OBJETIVO = argv[2];
    unsigned long tiempo_seg = 10; // valor por defecto
    if (argc >= 4) {
        tiempo_seg = strtoul(argv[3], NULL, 10);
        if (tiempo_seg == 0) tiempo_seg = 10;
    }
    const UINT32 PORT = 5084;        /* LLRP */
    const UINT32 TIMEOUT_MS = 5000;  /* 5 s */

    RFID_HANDLE32 h = NULL;
    CONNECTION_INFO ci = {0};
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
    // Si tu SDK expone esta función, descoméntala (en algunas versiones existe):
    // RFID_ClearTagFilters(h);

    // Iniciar inventario con trigger de tiempo configurable
    TRIGGER_INFO triggerInfo = {0};
    triggerInfo.startTrigger.type = 0; // START_TRIGGER_TYPE_IMMEDIATE
    triggerInfo.stopTrigger.type = 0;  // STOP_TRIGGER_TYPE_DURATION
    triggerInfo.stopTrigger.value.duration = (UINT32)(tiempo_seg * 1000); // tiempo en ms

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

    unsigned long static tag_counter = 0;
    bool static escrito = false; // bandera para evitar escrituras múltiples

    const ULONGLONG RUN_MS = tiempo_seg * 1000;
    ULONGLONG t0 = GetTickCount64();
    ULONGLONG lastTick = t0;
    unsigned long total = 0;
    unsigned long secCount = 0;

    while (GetTickCount64() - t0 < RUN_MS) {
        int gotOne = 0;
        while (RFID_API_SUCCESS == RFID_GetReadTag(h, tag)) {
            char epc[512];
            epc_to_hex(tag->pTagID, tag->tagIDLength, epc, sizeof(epc));

            // Mostrar EPC objetivo y EPC leído antes del filtro
            // printf("[DEBUG] EPC OBJETIVO: %s | EPC LEIDO: %s\n", EPC_OBJETIVO, epc);

            if (strcmp(epc, EPC_OBJETIVO) == 0) {
                // Solo escribir una vez
                if (!escrito) {
                    UINT16 sys_conf_word = 0x1390;
                    UINT8 sys_conf_data[2] = { (UINT8)(sys_conf_word >> 8), (UINT8)(sys_conf_word & 0xFF) };
                    if (WriteTag(h, tag->pTagID, tag->tagIDLength, MEMORY_BANK_USER, 0x0120, 0, sys_conf_data, 2)) {
                        printf("[OK] SYSTEM CONFIG WORD (0x1390) escrito en 0x0120\n");
                    } else {
                        printf("[ERR] Fallo al escribir SYSTEM CONFIG WORD\n");
                    }

                    UINT16 sensor_ctrl_word = 0x2000;
                    UINT8 sensor_ctrl_data[2] = { (UINT8)(sensor_ctrl_word >> 8), (UINT8)(sensor_ctrl_word & 0xFF) };
                    if (WriteTag(h, tag->pTagID, tag->tagIDLength, MEMORY_BANK_USER, 0x0123, 0, sensor_ctrl_data, 2)) {
                        printf("[OK] SENSOR CONTROL WORD (0x2000) escrito en 0x0123\n");
                    } else {
                        printf("[ERR] Fallo al escribir SENSOR CONTROL WORD\n");
                    }
                    escrito = true;
                }

                UINT16 xpc_w2 = (UINT16)((tag->XPC >> 16) & 0xFFFF);
                unsigned int bits_4_5 = (xpc_w2 >> 10) & 0x3;
                char error = (bits_4_5 == 0x0) ? '1' : ((bits_4_5 == 0x3) ? '0' : '?');
                int sensor_data = xpc_w2 & 0x3FF;
                if (sensor_data & 0x200) sensor_data |= ~0x3FF;
                float C_sense = (sensor_data - 128) * 0.15f;
                FILETIME ft;
                ULARGE_INTEGER ull;
                GetSystemTimeAsFileTime(&ft);
                ull.LowPart = ft.dwLowDateTime;
                ull.HighPart = ft.dwHighDateTime;
                const ULONGLONG EPOCH_DIFF = 11644473600000ULL;
                unsigned long long timestamp_ms = (ull.QuadPart / 10000ULL) - EPOCH_DIFF;
                printf("%llu | EPC: %s | xpc_w2: 0x%04X | error: %c | C_sense: %.2f | Antena: %u | RSSI: %d dBm | Contador: %lu\n",
                    timestamp_ms, epc, xpc_w2, error, C_sense, (unsigned)tag->antennaID, (int)tag->peakRSSI, ++tag_counter);
                static FILE* f = NULL;
                if (!f) {
                    fopen_s(&f, "taglog.csv", "w");
                    if (f) fprintf(f, "timestamp,epc,xpc_w2,error,C_sense,antenna,rssi,contador\n");
                }
                if (f) {
                    fprintf(f, "%llu,%s,0x%04X,%c,%.2f,%u,%d,%lu\n",
                        timestamp_ms, epc, xpc_w2, error, C_sense, (unsigned)tag->antennaID, (int)tag->peakRSSI, tag_counter);
                    fflush(f);
                }
            }
            ++total; ++secCount; gotOne = 1;
        }

        ULONGLONG now = GetTickCount64();
        if (now - lastTick >= 1000) {
            secCount = 0;
            lastTick = now;
        }

        if (!gotOne) Sleep(50);
    }

    rc = RFID_StopInventory(h);
    if (rc != RFID_API_SUCCESS) {
        printf("[WARN] RFID_StopInventory rc=%d\n", rc);
    }
    RFID_DeallocateTag(h, tag);

    rc = RFID_Disconnect(h);
    if (rc != RFID_API_SUCCESS) {
        printf("[WARN] RFID_Disconnect rc=%d\n", rc);
        return 4;
    }
    printf("[OK] Desconectado. Total tags vistos: %lu\n", total);
    return 0;
}
