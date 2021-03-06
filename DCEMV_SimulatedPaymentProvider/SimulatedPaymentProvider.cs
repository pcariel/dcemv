﻿/*
*************************************************************************
DC EMV
Open Source EMV
Copyright (C) 2018  Vicente Da Silva

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published
by the Free Software Foundation, either version 3 of the License, or
any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see http://www.gnu.org/licenses/
*************************************************************************
*/
using DCEMV.EMVProtocol.Kernels;
using DCEMV.EMVSecurity;
using DCEMV.FormattingUtils;
using DCEMV.Shared;
using DCEMV.TLVProtocol;
using System;

namespace DCEMV.SimulatedPaymentProvider
{

    public class SimulatedApprover : IOnlineApprover
    {
        private static Logger Logger = new Logger(typeof(SimulatedApprover));

        private byte[] arcApproved = Formatting.HexStringToByteArray("3030"); //approved code
        private byte[] arcDeclined = Formatting.HexStringToByteArray("3035"); //declined code

        public ApproverResponseBase DoAuth(ApproverRequestBase request)
        {
            if (request is EMVApproverRequest)
                return DoEMVAuth(request);
            if (request is QRCodeApproverRequest)
                return DoQRAuth(request);
            else
                throw new NotImplementedException();
        }

        public ApproverResponseBase DoReversal(ApproverRequestBase request, bool isOnline)
        {
            throw new NotImplementedException();
        }

        public ApproverResponseBase DoAdvice(ApproverRequestBase request, bool isOnline)
        {
            throw new NotImplementedException();
        }

        public ApproverResponseBase DoCheckAuthStatus(ApproverRequestBase request)
        {
            if (request is EMVApproverRequest)
                throw new NotImplementedException();
            if (request is QRCodeApproverRequest)
                return DoQRCheckAuthStatus(request);
            else
                throw new NotImplementedException();
           
        }

        private ApproverResponseBase DoQRCheckAuthStatus(ApproverRequestBase request)
        {
            return new QRCodeApproverResponse()
            {
                IsApproved = true,
                ResponseMessage = "Approved",
            };
        }

        private ApproverResponseBase DoQRAuth(ApproverRequestBase requestIn)
        {
            return new QRCodeApproverResponse()
            {
                IsApproved = true,
                ResponseMessage = "Approved",
            };
        }

        private ApproverResponseBase DoEMVAuth(ApproverRequestBase requestIn)
        {
            EMVApproverRequest request = ((EMVApproverRequest)requestIn);

            CryptoMetaData cryptoMetaData = EMVDESSecurity.BuildCryptoMeta(request.EMV_Data);

            //Do additional checking here, e.g. customer balances etc
            //if decline set isApproved to false
            bool isApproved = true;
            //do we want to send back a pin change script, 
            string newPin = "";// = "4315";
            //decide whether to send 71 or 72 script template, 71 scripts applied before 2nd gen ac , 72 scripts applied after 2nd gen ac
            bool doPinChangeBefore = false;

            TLV _8A;
            string responseMessage;
            if (isApproved)
            {
                _8A = TLV.Create(EMVTagsEnum.AUTHORISATION_RESPONSE_CODE_8A_KRN.Tag, arcApproved);
                responseMessage = "Approved";
            }
            else
            {
                _8A = TLV.Create(EMVTagsEnum.AUTHORISATION_RESPONSE_CODE_8A_KRN.Tag, arcDeclined);
                responseMessage = "Declined";
            }

            TLV _91;
            byte[] arpc;
            //returns null if arqc cannot be verified
            if (cryptoMetaData.CryptoVersion == CrptoVersionEnum._18)
                arpc = EMVDESSecurity.VerifyCryptogramGenARPC(request.EMV_Data, cryptoMetaData, PackCSU());
            else
                arpc = EMVDESSecurity.VerifyCryptogramGenARPC(request.EMV_Data, cryptoMetaData, _8A.Value);

            if (arpc != null)
            {
                _91 = Pack91(cryptoMetaData, arpc, _8A);// TLV.Create(EMVTagsEnum.ISSUER_AUTHENTICATION_DATA_91_KRN.Tag, Formatting.ConcatArrays(arpc, _8A.Value));
                Logger.Log("Tx approved: " + isApproved + " ARQC passed, ARPC is " + Formatting.ByteArrayToHexString(arpc));
            }
            else
            {
                isApproved = false;
                responseMessage = "Tx Declined: ARQC Failure";
                _8A = TLV.Create(EMVTagsEnum.AUTHORISATION_RESPONSE_CODE_8A_KRN.Tag, new byte[] { 0x20, 0x20 });
                _91 = Pack91(cryptoMetaData, arpc, _8A); //TLV.Create(EMVTagsEnum.ISSUER_AUTHENTICATION_DATA_91_KRN.Tag, new byte[8]);
                Logger.Log("ARQC failed");
            }

            byte[] _86 = new byte[0];
            //don't allow pin change if arqc could not be validated
            if (!string.IsNullOrWhiteSpace(newPin) && arpc != null)
            {
                try
                {
                    TLV _9F26 = request.EMV_Data.Children.Get(EMVTagsEnum.APPLICATION_CRYPTOGRAM_9F26_KRN.Tag);
                    if (_9F26 == null)
                        throw new Exception("No Cryptogram found");
                    //TODO: for mchip we must increment the arqc by one for each subsequent command created
                    _86 = EMVDESSecurity.CalculatePinChangeScript(request.EMV_Data, cryptoMetaData, newPin, _9F26.Value);
                }
                catch
                {
                    _86 = new byte[0];
                }
            }

            TLV _71TLV;
            TLV _72TLV;
            if (doPinChangeBefore)
            {
                _71TLV = TLV.Create(EMVTagsEnum.ISSUER_SCRIPT_TEMPLATE_1_71_KRN.Tag);
                _71TLV.Deserialize(Formatting.ConcatArrays(new byte[] { 0x71, (byte)_86.Length }, _86), 0);

                _72TLV = TLV.Create(EMVTagsEnum.ISSUER_SCRIPT_TEMPLATE_2_72_KRN.Tag);
                _72TLV.Deserialize(Formatting.ConcatArrays(new byte[] { 0x72, 0x00 }, new byte[0]), 0);
            }
            else
            {
                _72TLV = TLV.Create(EMVTagsEnum.ISSUER_SCRIPT_TEMPLATE_2_72_KRN.Tag);
                _72TLV.Deserialize(Formatting.ConcatArrays(new byte[] { 0x72, (byte)_86.Length }, _86), 0);

                _71TLV = TLV.Create(EMVTagsEnum.ISSUER_SCRIPT_TEMPLATE_1_71_KRN.Tag);
                _71TLV.Deserialize(Formatting.ConcatArrays(new byte[] { 0x71, 0x00 }, new byte[0]), 0);
            }

            return new EMVApproverResponse()
            {
                IsApproved = isApproved,
                ResponseMessage = responseMessage,
                AuthCode_8A = _8A,
                IssuerAuthData_91 = _91,
                IssuerScriptTemplate_72 = _72TLV,
                IssuerScriptTemplate_71 = _71TLV,
            };
        }

        private TLV Pack91(CryptoMetaData cryptoMetaData, byte[] arpc, TLV _8A)
        {
            TLV _91;

            if (arpc == null)
            {
                arpc = new byte[8];
            }

            switch (cryptoMetaData.SKDMethod)
            {
                case SKDMethod.VSDC:
                    _91 = TLV.Create(EMVTagsEnum.ISSUER_AUTHENTICATION_DATA_91_KRN.Tag, Formatting.ConcatArrays(arpc, _8A.Value));
                    break;

                case SKDMethod.MCHIP:
                    _91 = TLV.Create(EMVTagsEnum.ISSUER_AUTHENTICATION_DATA_91_KRN.Tag, Formatting.ConcatArrays(arpc, _8A.Value));
                    break;

                case SKDMethod.EMV_CSKD:
                    _91 = TLV.Create(EMVTagsEnum.ISSUER_AUTHENTICATION_DATA_91_KRN.Tag, Formatting.ConcatArrays(arpc, PackCSU()));
                    break;

                default:
                    throw new SimulatedPaymentProviderException("Pack91: SKDMethod not supported:" + cryptoMetaData.SKDMethod);
            }
            return _91;
        }

        private byte[] PackCSU()
        {
            //Byte 1:
            //bit 8: 1b = Proprietary Authentication
            //Data included
            //bits 7-5: RFU (000b)
            //bits 4-1: PIN Try Counter
            byte byte1 = 0x03;
            //Byte 2:
            //bit 8: 1b = Issuer approves online
            //transaction
            //bit 7: 1b = Card block
            //bit 6: 1b = Application block
            //bit 5: 1b = Update PIN Try Counter
            //bit 4: 1b = Set Go Online on Next
            //Transaction
            //bit 3: 1b = CSU generated by proxy
            //for the issuer
            //Note: When Byte 2 bit 3 is set to 1b,
            //issuers can use the ADA(instead of the
            //following two bits) to control what
            //processing occurs for counter updates.
            //bits 2–1: Update Counters
            //00b = Do not update velocitychecking
            //counters
            //01b = Set velocity-checking
            //counters to Upper Limits
            //10b = Reset velocity-checking
            //counters to Zero
            //11b = Add transaction to velocity checking
            //counters
            byte byte2 = 0x91; //1001 0001
            //Byte 3: RFU ('00')
            byte byte3 = 0x00;
            //Byte 4: Issuer-Discretionary(or '00')
            byte byte4 = 0x00;

            return new byte[] { byte1, byte2, byte3, byte4 };
        }

        
    }
}
