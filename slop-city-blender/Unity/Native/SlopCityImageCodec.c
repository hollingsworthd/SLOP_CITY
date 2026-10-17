// Original SLOP CITY image normalization, built with Apple's system ImageIO.
// Converts public TikTok photo posts to JPEG for Unity's texture decoder.
#include <CoreFoundation/CoreFoundation.h>
#include <ImageIO/ImageIO.h>
#include <AudioToolbox/AudioToolbox.h>
#include <stdlib.h>

__attribute__((visibility("default")))
int SlopCityConvertImage(const char *input, const char *output) {
    int result = 0;
    if (!input || !output) return 0;
    CFStringRef inString = CFStringCreateWithCString(NULL, input, kCFStringEncodingUTF8);
    CFStringRef outString = CFStringCreateWithCString(NULL, output, kCFStringEncodingUTF8);
    if (!inString || !outString) goto strings;
    CFURLRef inURL = CFURLCreateWithFileSystemPath(NULL, inString, kCFURLPOSIXPathStyle, false);
    CFURLRef outURL = CFURLCreateWithFileSystemPath(NULL, outString, kCFURLPOSIXPathStyle, false);
    CGImageSourceRef source = inURL ? CGImageSourceCreateWithURL(inURL, NULL) : NULL;
    if (source && outURL) {
        CFDictionaryRef properties = CGImageSourceCopyPropertiesAtIndex(source, 0, NULL);
        int width = 0, height = 0;
        CFNumberRef widthValue = properties ? CFDictionaryGetValue(properties, kCGImagePropertyPixelWidth) : NULL;
        CFNumberRef heightValue = properties ? CFDictionaryGetValue(properties, kCGImagePropertyPixelHeight) : NULL;
        int valid = widthValue && heightValue && CFGetTypeID(widthValue) == CFNumberGetTypeID()
            && CFGetTypeID(heightValue) == CFNumberGetTypeID()
            && CFNumberGetValue(widthValue, kCFNumberIntType, &width)
            && CFNumberGetValue(heightValue, kCFNumberIntType, &height)
            && width > 0 && height > 0 && width <= 8192 && height <= 8192
            && (UInt64)width * (UInt64)height <= 32ULL * 1024 * 1024;
        if (properties) CFRelease(properties);
        if (!valid) goto images;
        int limit = 2048;
        CFNumberRef maximum = CFNumberCreate(NULL, kCFNumberIntType, &limit);
        if (!maximum) goto images;
        const void *keys[] = { kCGImageSourceCreateThumbnailFromImageAlways, kCGImageSourceCreateThumbnailWithTransform, kCGImageSourceThumbnailMaxPixelSize };
        const void *values[] = { kCFBooleanTrue, kCFBooleanTrue, maximum };
        CFDictionaryRef options = CFDictionaryCreate(NULL, keys, values, 3, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
        if (!options) { CFRelease(maximum); goto images; }
        CGImageRef image = CGImageSourceCreateThumbnailAtIndex(source, 0, options);
        if (image) {
            CGImageDestinationRef target = CGImageDestinationCreateWithURL(outURL, CFSTR("public.jpeg"), 1, NULL);
            if (target) {
                CGImageDestinationAddImage(target, image, NULL);
                result = CGImageDestinationFinalize(target) ? 1 : 0;
                CFRelease(target);
            }
            CGImageRelease(image);
        }
        CFRelease(options); CFRelease(maximum);
    }
images:
    if (source) CFRelease(source);
    if (inURL) CFRelease(inURL);
    if (outURL) CFRelease(outURL);
strings:
    if (inString) CFRelease(inString);
    if (outString) CFRelease(outString);
    return result;
}

// AAC/M4A photo-post music is converted to PCM WAV; renaming it .mp3 would
// silently break Unity's MPEG loader. ExtAudioFile uses the system decoders.
__attribute__((visibility("default")))
int SlopCityConvertAudio(const char *input, const char *output) {
    int result = -1;
    if (!input || !output) return 0;
    CFStringRef inString = CFStringCreateWithCString(NULL, input, kCFStringEncodingUTF8);
    CFStringRef outString = CFStringCreateWithCString(NULL, output, kCFStringEncodingUTF8);
    CFURLRef inURL = inString ? CFURLCreateWithFileSystemPath(NULL, inString, kCFURLPOSIXPathStyle, false) : NULL;
    CFURLRef outURL = outString ? CFURLCreateWithFileSystemPath(NULL, outString, kCFURLPOSIXPathStyle, false) : NULL;
    ExtAudioFileRef source = NULL, target = NULL;
    void *samples = NULL;
    if (!inURL || !outURL) goto cleanup;
    OSStatus error = ExtAudioFileOpenURL(inURL, &source);
    if (error != noErr) { result = error; goto cleanup; }
    AudioStreamBasicDescription original = {0};
    UInt32 propertySize = sizeof(original);
    error = ExtAudioFileGetProperty(source, kExtAudioFileProperty_FileDataFormat, &propertySize, &original);
    if (error != noErr) { result = error; goto cleanup; }
    if (original.mChannelsPerFrame < 1 || original.mChannelsPerFrame > 2
        || original.mSampleRate < 8000 || original.mSampleRate > 192000) goto cleanup;
    SInt64 sourceFrames = 0;
    propertySize = sizeof(sourceFrames);
    error = ExtAudioFileGetProperty(source, kExtAudioFileProperty_FileLengthFrames, &propertySize, &sourceFrames);
    if (error != noErr || sourceFrames < 1 || sourceFrames / original.mSampleRate > 300) goto cleanup;
    AudioStreamBasicDescription pcm = {0};
    pcm.mSampleRate = 44100;
    pcm.mFormatID = kAudioFormatLinearPCM;
    pcm.mFormatFlags = kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked;
    pcm.mChannelsPerFrame = original.mChannelsPerFrame;
    pcm.mBitsPerChannel = 16;
    pcm.mFramesPerPacket = 1;
    pcm.mBytesPerFrame = pcm.mBytesPerPacket = 2 * pcm.mChannelsPerFrame;
    error = ExtAudioFileSetProperty(source, kExtAudioFileProperty_ClientDataFormat, sizeof(pcm), &pcm);
    if (error != noErr) { result = error; goto cleanup; }
    error = ExtAudioFileCreateWithURL(outURL, kAudioFileWAVEType, &pcm, NULL, kAudioFileFlags_EraseFile, &target);
    if (error != noErr) { result = error; goto cleanup; }
    error = ExtAudioFileSetProperty(target, kExtAudioFileProperty_ClientDataFormat, sizeof(pcm), &pcm);
    if (error != noErr) { result = error; goto cleanup; }
    const UInt32 block = 16384;
    samples = malloc(block * pcm.mBytesPerFrame);
    if (!samples) goto cleanup;
    UInt64 totalFrames = 0;
    for (;;) {
        AudioBufferList buffer = {0};
        buffer.mNumberBuffers = 1;
        buffer.mBuffers[0].mNumberChannels = pcm.mChannelsPerFrame;
        buffer.mBuffers[0].mDataByteSize = block * pcm.mBytesPerFrame;
        buffer.mBuffers[0].mData = samples;
        UInt32 frames = block;
        error = ExtAudioFileRead(source, &frames, &buffer);
        if (error != noErr) { result = error; goto cleanup; }
        if (frames == 0) { result = totalFrames > 0; break; }
        totalFrames += frames;
        if (totalFrames > 44100ULL * 60 * 5) { result = 0; goto cleanup; }
        error = ExtAudioFileWrite(target, frames, &buffer);
        if (error != noErr) { result = error; goto cleanup; }
    }
cleanup:
    if (samples) free(samples);
    if (target && ExtAudioFileDispose(target) != noErr) result = 0;
    if (source) ExtAudioFileDispose(source);
    if (inURL) CFRelease(inURL);
    if (outURL) CFRelease(outURL);
    if (inString) CFRelease(inString);
    if (outString) CFRelease(outString);
    return result;
}
