#include <ntddk.h>
#include <wdf.h>
#include <vhf.h>
#include <wdmsec.h>

// Administrative, local device endpoint. One client at a time.
#define IOCTL_SUBMIT_KEYBOARD CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_WRITE_ACCESS)
typedef struct { VHFHANDLE Vhf; } DEVICE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, GetContext);
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD DeviceAdd;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL DeviceControl;
EVT_WDF_FILE_CLEANUP FileCleanup;
EVT_WDF_OBJECT_CONTEXT_CLEANUP DeviceCleanup;

// Standard 8-byte boot-style keyboard report: modifiers, reserved, six usages.
// No vendor identity is copied and no proprietary device protocol is used.
static UCHAR Descriptor[] = {
    0x05,0x01, 0x09,0x06, 0xA1,0x01,
    0x05,0x07, 0x19,0xE0, 0x29,0xE7,
    0x15,0x00, 0x25,0x01, 0x75,0x01, 0x95,0x08, 0x81,0x02,
    0x95,0x01, 0x75,0x08, 0x81,0x01,
    0x95,0x06, 0x75,0x08, 0x15,0x00, 0x25,0x65,
    0x19,0x00, 0x29,0x65, 0x81,0x00, 0xC0
};

static NTSTATUS Submit(WDFDEVICE device, PUCHAR bytes)
{
    HID_XFER_PACKET packet = {0};
    packet.reportBuffer = bytes;
    packet.reportBufferLen = 8;
    packet.reportId = 0;
    return VhfReadReportSubmit(GetContext(device)->Vhf, &packet);
}

NTSTATUS DriverEntry(PDRIVER_OBJECT driver, PUNICODE_STRING registry)
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, DeviceAdd);
    return WdfDriverCreate(driver, registry, WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
}

NTSTATUS DeviceAdd(WDFDRIVER driver, PWDFDEVICE_INIT init)
{
    NTSTATUS status;
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_FILEOBJECT_CONFIG files;
    WDF_IO_QUEUE_CONFIG queue;
    VHF_CONFIG vhf;
    DECLARE_CONST_UNICODE_STRING(name, L"\\Device\\InputProbeHid");
    DECLARE_CONST_UNICODE_STRING(link, L"\\DosDevices\\InputProbeHid");
    DECLARE_CONST_UNICODE_STRING(security, L"D:P(A;;GA;;;SY)(A;;GA;;;BA)");
    UNREFERENCED_PARAMETER(driver);

    WdfDeviceInitSetDeviceType(init, FILE_DEVICE_UNKNOWN);
    WdfDeviceInitSetCharacteristics(init, FILE_DEVICE_SECURE_OPEN, FALSE);
    WdfDeviceInitSetExclusive(init, TRUE);
    status = WdfDeviceInitAssignName(init, &name);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceInitAssignSDDLString(init, &security);
    if (!NT_SUCCESS(status)) return status;
    WDF_FILEOBJECT_CONFIG_INIT(&files, WDF_NO_EVENT_CALLBACK, WDF_NO_EVENT_CALLBACK, FileCleanup);
    WdfDeviceInitSetFileObjectConfig(init, &files, WDF_NO_OBJECT_ATTRIBUTES);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, DEVICE_CONTEXT);
    attributes.EvtCleanupCallback = DeviceCleanup;
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfDeviceCreate(&init, &attributes, &device);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceCreateSymbolicLink(device, &link);
    if (!NT_SUCCESS(status)) return status;
    VHF_CONFIG_INIT(&vhf, WdfDeviceWdmGetDeviceObject(device), sizeof(Descriptor), Descriptor);
    status = VhfCreate(&vhf, &GetContext(device)->Vhf);
    if (!NT_SUCCESS(status)) return status;
    status = VhfStart(GetContext(device)->Vhf);
    if (!NT_SUCCESS(status)) return status;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue, WdfIoQueueDispatchSequential);
    queue.EvtIoDeviceControl = DeviceControl;
    return WdfIoQueueCreate(device, &queue, WDF_NO_OBJECT_ATTRIBUTES, WDF_NO_HANDLE);
}

VOID DeviceControl(WDFQUEUE queue, WDFREQUEST request, size_t outputLength, size_t inputLength, ULONG code)
{
    PUCHAR bytes;
    NTSTATUS status;
    ULONG i;
    UNREFERENCED_PARAMETER(outputLength);
    if (code != IOCTL_SUBMIT_KEYBOARD) { WdfRequestComplete(request, STATUS_INVALID_DEVICE_REQUEST); return; }
    if (inputLength != 8) { WdfRequestComplete(request, STATUS_INVALID_BUFFER_SIZE); return; }
    status = WdfRequestRetrieveInputBuffer(request, 8, (PVOID*)&bytes, NULL);
    if (!NT_SUCCESS(status)) { WdfRequestComplete(request, status); return; }
    if (bytes[1] != 0) { WdfRequestComplete(request, STATUS_INVALID_PARAMETER); return; }
    for (i = 2; i < 8; ++i)
        if (bytes[i] > 0x65) { WdfRequestComplete(request, STATUS_INVALID_PARAMETER); return; }
    status = Submit(WdfIoQueueGetDevice(queue), bytes);
    WdfRequestComplete(request, status);
}

VOID FileCleanup(WDFFILEOBJECT file)
{
    UCHAR released[8] = {0};
    WDFDEVICE device = WdfFileObjectGetDevice(file);
    if (GetContext(device)->Vhf) (void)Submit(device, released);
}

VOID DeviceCleanup(WDFOBJECT object)
{
    DEVICE_CONTEXT* context = GetContext(object);
    if (context->Vhf) { VhfDelete(context->Vhf, TRUE); context->Vhf = NULL; }
}
