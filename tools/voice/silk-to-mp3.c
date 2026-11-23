/* Standalone SILK v3 to MP3 converter used by WeChat Cleaner. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include <windows.h>
#include <fcntl.h>
#include <io.h>
#include <stdint.h>
#include "SKP_Silk_SDK_API.h"
#include "lame.h"

#define SAMPLE_RATE 24000
#define FRAME_SAMPLES (SAMPLE_RATE / 50)
#define MAX_PACKET 1024
#define MAX_FRAMES (50 * 60 * 60)

static int encode(FILE *in, FILE *out) {
    static const char silk_header[] = "#!SILK_V3";
    unsigned char header[sizeof(silk_header) - 1];
    unsigned char packet[MAX_PACKET], mp3[8192];
    SKP_int16 pcm[FRAME_SAMPLES];
    SKP_SILK_SDK_DecControlStruct ctrl = {0};
    SKP_int32 state_size = 0;
    void *state = NULL;
    lame_t lame = NULL;
    unsigned frames = 0;
    int b, rc = 3;

    b = fgetc(in);
    if (b == 2) b = fgetc(in); /* Tencent prefix */
    if (b == EOF) return 3;
    header[0] = (unsigned char)b;
    if (fread(header + 1, 1, sizeof(header) - 1, in) != sizeof(header) - 1 ||
        memcmp(header, silk_header, sizeof(header)) != 0) return 3;

    if (SKP_Silk_SDK_Get_Decoder_Size(&state_size) || state_size <= 0) return 4;
    state = calloc(1, (size_t)state_size);
    if (!state || SKP_Silk_SDK_InitDecoder(state)) goto done;
    ctrl.API_sampleRate = SAMPLE_RATE;

    lame = lame_init();
    if (!lame || lame_set_num_channels(lame, 1) < 0 ||
        lame_set_in_samplerate(lame, SAMPLE_RATE) < 0 || lame_set_out_samplerate(lame, SAMPLE_RATE) < 0 ||
        lame_set_brate(lame, 48) < 0 || lame_set_quality(lame, 3) < 0 ||
        lame_set_bWriteVbrTag(lame, 0) < 0 || lame_init_params(lame) < 0) { rc = 4; goto done; }

    for (;;) {
        unsigned size, internal;
        int low = fgetc(in), high;
        if (low == EOF) { if (ferror(in)) goto done; break; }
        high = fgetc(in);
        if (high == EOF) goto done;
        size = (unsigned)low + ((unsigned)high << 8);
        if (size == 0xffff) { if (fgetc(in) != EOF || ferror(in)) goto done; break; }
        if (size == 0 || size > MAX_PACKET || fread(packet, 1, size, in) != size) goto done;
        for (internal = 0; ; ++internal) {
            SKP_int16 samples = 0;
            int len;
            if (internal >= 5 || frames >= MAX_FRAMES ||
                SKP_Silk_SDK_Decode(state, &ctrl, 0, packet, (SKP_int)size, pcm, &samples) ||
                samples <= 0 || samples > FRAME_SAMPLES) goto done;
            len = lame_encode_buffer(lame, pcm, pcm, samples, mp3, sizeof(mp3));
            if (len < 0 || fwrite(mp3, 1, (size_t)len, out) != (size_t)len) { rc = 5; goto done; }
            ++frames;
            if (!ctrl.moreInternalDecoderFrames) break;
        }
    }
    if (frames != 0) {
        int len = lame_encode_flush(lame, mp3, sizeof(mp3));
        rc = len >= 0 && fwrite(mp3, 1, (size_t)len, out) == (size_t)len ? 0 : 5;
    }
done:
    if (lame) lame_close(lame);
    free(state);
    return rc;
}

int wmain(int argc, wchar_t **argv) {
    FILE *in, *out;
    HANDLE handle;
    int fd, rc;
    if (argc != 3) return 2;
    in = _wfopen(argv[1], L"rb");
    if (!in) return 2;
    // New output only. The app uses unique scratch paths for both files.
    handle = CreateFileW(argv[2], GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (handle == INVALID_HANDLE_VALUE) { fclose(in); return 2; }
    fd = _open_osfhandle((intptr_t)handle, _O_WRONLY | _O_BINARY);
    if (fd < 0) { CloseHandle(handle); fclose(in); _wremove(argv[2]); return 2; }
    out = _fdopen(fd, "wb");
    if (!out) { _close(fd); fclose(in); _wremove(argv[2]); return 2; }
    rc = encode(in, out);
    fclose(in);
    if (fclose(out) != 0) rc = 5;
    if (rc != 0) _wremove(argv[2]);
    return rc;
}
